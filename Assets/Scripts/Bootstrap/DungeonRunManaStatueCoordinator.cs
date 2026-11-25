using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Map;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns one explicit map destructible action and its input/feedback lifetime.
    /// The historical component name and script GUID remain stable. Each Dungeon
    /// owner retains its collision revisions, pickup journal and node completion. A
    /// click captures one bump or one collection; feedback never submits another
    /// action. Persist must be a pure atomic save: return means publication,
    /// throwing means no publication. Saved actions survive view closure.
    /// </summary>
    public sealed class DungeonRunManaStatueCoordinator : MonoBehaviour
    {
        private DungeonRunSession _run;
        private LevelRunSession _boundFloor;
        private MapPresentation _presentation;
        private CombatScenePreloader _preloader;
        private Action _persist;
        private Func<bool> _canPersist;
        private Canvas _canvas;
        private RewardFeedbackPresenter _reveal;
        private IDisposable _flowLease;
        private Coroutine _routine;
        private MapAction _action;
        private bool _watchFeedback;
        private int _generation;

        // Captured read-only intent; this component never owns either rule journal.
        private readonly struct ActionOffer
        {
            internal readonly LevelInteractionEffectKind Kind;
            internal readonly int AcceptedBumps;
            internal readonly bool CanBump, CanCollect;
            internal ActionOffer(LevelInteractionEffectKind kind, int bumps, bool canBump, bool canCollect)
            { Kind = kind; AcceptedBumps = bumps; CanBump = canBump; CanCollect = canCollect; }
        }

        private sealed class MapAction
        {
            internal readonly DungeonRunSession Run;
            internal readonly LevelRunSession Floor;
            internal readonly MapPresentation Presentation;
            internal readonly MapPreviewProfile Profile;
            internal readonly string NodeId;
            internal readonly LevelInteractionEffectKind Kind;
            internal readonly int PreviousBumps;
            internal readonly bool Collect;
            internal readonly int Generation;
            internal bool Committing, Saved, CloseRequested, Finished;

            internal MapAction(DungeonRunSession run, MapPresentation presentation,
                string nodeId, ActionOffer offer, int generation)
            {
                Run = run; Floor = run.CurrentFloor; Presentation = presentation;
                Profile = presentation.Profile; NodeId = nodeId;
                Kind = offer.Kind; PreviousBumps = offer.AcceptedBumps; Collect = offer.CanCollect;
                Generation = generation;
            }
        }

        public bool IsActive => _action != null;

        /// <summary>Only map synchronization belongs to this notification; do not save again.</summary>
        public event Action MapStateCommitted;

        /// <summary>Once per saved action after feedback/closure; consumers may restart preloading.</summary>
        public event Action InteractionFinished;

        public void Bind(DungeonRunSession run, MapPresentation presentation,
            CombatScenePreloader preloader, Action persist, Func<bool> canPersist)
        {
            if (_action?.Committing == true)
                throw new InvalidOperationException("Cannot rebind during a destructible commit.");
            CloseSilently();
            _run = run; _boundFloor = run?.CurrentFloor; _presentation = presentation;
            _preloader = preloader; _persist = persist; _canPersist = canPersist;
        }

        public static bool Handles(LevelNode node) => FindKind(node).HasValue;

        private static LevelInteractionEffectKind? FindKind(LevelNode node)
        {
            if (node?.completionEffects == null) return null;
            foreach (var effect in node.completionEffects)
                if (effect?.kind == LevelInteractionEffectKind.ManaStatue ||
                    effect?.kind == LevelInteractionEffectKind.AbandonedCart) return effect.kind;
            return null;
        }

        private bool TryInspect(string nodeId, out ActionOffer offer, out string error)
        {
            offer = default;
            var kind = FindKind(_boundFloor.FindNode(nodeId));
            if (kind == LevelInteractionEffectKind.AbandonedCart)
            {
                if (!_run.AbandonedCarts.TryInspect(nodeId, out var cart, out error)) return false;
                offer = new ActionOffer(kind.Value, cart.AcceptedBumps, cart.CanBump, cart.CanCollect);
                return true;
            }
            if (kind == LevelInteractionEffectKind.ManaStatue)
            {
                if (!_run.ManaStatues.TryInspect(nodeId, out var statue, out error)) return false;
                offer = new ActionOffer(kind.Value, statue.AcceptedBumps, statue.CanBump, statue.CanCollect);
                return true;
            }
            error = "当前节点不是可操作的破坏物。";
            return false;
        }

        public bool TryBegin(string nodeId, out string error)
        {
            error = null;
            if (IsActive) { error = "已有破坏物动作正在处理。"; return false; }
            MapAction action = null;
            try
            {
                RequireBinding(nodeId);
                if (!_presentation.IsInteractionEnabled)
                    throw new InvalidOperationException("地图交互当前被另一流程关闭。");
                RequirePersistence();
                if (!TryInspect(nodeId, out var offer, out error)) return false;
                if (!offer.CanBump && !offer.CanCollect)
                { error = "此破坏物的奖励已经领取。"; return false; }

                action = new MapAction(_run, _presentation, nodeId, offer, ++_generation);
                _action = action;
                _flowLease = SceneTransitionRuntime.ExternalModalBarrier?.Hold("modal.map.destructible");
                _presentation.SetInteractionEnabled(false);
                _preloader?.InvalidatePreparedRunState();
                RequireCurrent(action);
                _routine = StartCoroutine(PerformAction(action));
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (action != null && IsCurrent(action)) Finish(action);
                return false;
            }
        }

        private IEnumerator PerformAction(MapAction action)
        {
            // StartCoroutine must assign its handle before any synchronous close.
            yield return null;
            while (IsCurrent(action) && _preloader != null && _preloader.IsBusy)
                yield return null;
            if (!IsCurrent(action)) yield break;
            _routine = null;

            bool committed = false;
            LevelInteractionReceipt receipt = default;
            CartPickupReceipt cartReceipt = default;
            string failure = null;
            try
            {
                RequireCurrent(action);
                if (!TryInspect(action.NodeId, out var offer, out failure))
                    throw new InvalidOperationException(failure);
                if (offer.Kind != action.Kind || offer.AcceptedBumps != action.PreviousBumps ||
                    offer.CanCollect != action.Collect || (!action.Collect && !offer.CanBump))
                    throw new InvalidOperationException("破坏物动作已变化，请重新点击当前动作。");

                action.Committing = true;
                try
                {
                    Action save = () => { RequireCurrent(action); _persist(); };
                    if (action.Kind == LevelInteractionEffectKind.AbandonedCart)
                        committed = action.Collect
                            ? _run.AbandonedCarts.TryCollect(action.NodeId, out cartReceipt, out failure, save)
                            : _run.AbandonedCarts.TryBump(action.NodeId, action.PreviousBumps, out _, out failure, save);
                    else
                        committed = action.Collect
                            ? _run.ManaStatues.TryCollectAndComplete(action.NodeId, out receipt, out failure, save)
                            : _run.ManaStatues.TryBump(action.NodeId, action.PreviousBumps, out _, out failure, save);
                }
                finally { action.Committing = false; }
            }
            catch (Exception exception) { failure = exception.Message; }

            if (!committed)
            {
                if (action.CloseRequested) Finish(action);
                else ShowFailure(action, failure);
                yield break;
            }

            action.Saved = true;
            Publish(MapStateCommitted, "map synchronization");
            if (action.CloseRequested || !IsCurrent(action) || !isActiveAndEnabled)
                Finish(action);
            else if (action.Collect)
            {
                try
                {
                    ShowFeedback(action, action.Kind == LevelInteractionEffectKind.AbandonedCart
                        ? FromCartPickup(cartReceipt)
                        : RewardFeedbackItem.FromInteractionReceipt(receipt));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[MapDestructible] 已保存的结果反馈不可用：" + exception.Message);
                    Finish(action);
                }
            }
            else
                Finish(action);
        }

        private static RewardFeedbackItem FromCartPickup(CartPickupReceipt receipt)
        {
            return new RewardFeedbackItem
            {
                ReceiptId = receipt.EffectId,
                Kind = RewardFeedbackKind.Stat,
                Title = "领取护腕",
                DisplayName = $"每回合自动抽牌 +{receipt.ActualAmount}",
                Description = $"本局抽牌加成：{receipt.ValueBefore} → {receipt.ValueAfter}\n后续战斗每回合自动抽牌增加；不立即抽牌。",
                PresentationId = RewardArtworkCatalog.HandPresentationId,
                Artwork = RewardArtworkCatalog.Resolve(RewardArtworkCatalog.HandPresentationId),
                Quantity = receipt.ActualAmount,
                ValueBefore = receipt.ValueBefore,
                ValueAfter = receipt.ValueAfter,
                Accent = new Color(0.45f, 0.73f, 0.66f, 1f)
            };
        }

        private void RequireBinding(string nodeId)
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy || _run == null ||
                _presentation == null || !_presentation.isActiveAndEnabled || !_presentation.IsBuilt ||
                _presentation.Profile == null || !ReferenceEquals(_run.CurrentFloor, _boundFloor))
                throw new InvalidOperationException("破坏物尚未绑定当前地图楼层。");
            if (string.IsNullOrWhiteSpace(nodeId) || !Handles(_boundFloor.FindNode(nodeId)) ||
                !HasMapGroup(_presentation.Profile, nodeId))
                throw new InvalidOperationException("当前地图没有对应的破坏物入口。");
        }

        private static bool HasMapGroup(MapPreviewProfile profile, string nodeId)
        {
            if (profile.nodes == null) return false;
            foreach (var node in profile.nodes)
                if (node != null && string.Equals(node.interactionGroupId, nodeId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private void RequirePersistence()
        {
            if (_persist == null || _canPersist == null || !_canPersist())
                throw new InvalidOperationException("破坏物动作需要正式可保存的上下文。");
        }

        private bool IsCurrent(MapAction action) => ReferenceEquals(_action, action) &&
            action.Generation == _generation && !action.Finished;

        private bool HasSameMap(MapAction action) => ReferenceEquals(_run, action.Run) &&
            ReferenceEquals(_run?.CurrentFloor, action.Floor) &&
            ReferenceEquals(_presentation, action.Presentation) && _presentation != null &&
            ReferenceEquals(_presentation.Profile, action.Profile);

        private void RequireCurrent(MapAction action)
        {
            if (!IsCurrent(action) || action.CloseRequested || !HasSameMap(action))
                throw new InvalidOperationException("破坏物动作不再属于当前地图。");
            RequireBinding(action.NodeId);
            RequirePersistence();
            if (!IsCurrent(action) || action.CloseRequested || !HasSameMap(action) ||
                FindKind(_boundFloor.FindNode(action.NodeId)) != action.Kind)
                throw new InvalidOperationException("破坏物动作在资格检查后已经变化或关闭。");
        }

        private void ShowFailure(MapAction action, string error)
        {
            Debug.LogWarning("[MapDestructible] 动作未完成：" + error);
            ShowFeedback(action, new RewardFeedbackItem
            {
                Kind = RewardFeedbackKind.Unavailable, Title = "破坏物动作未完成",
                DisplayName = "返回地图后可重试",
                Description = "当前动作未完成，请返回地图后重试。"
            });
        }

        private void ShowFeedback(MapAction action, RewardFeedbackItem item)
        {
            if (!IsCurrent(action) || !HasSameMap(action) || !isActiveAndEnabled)
            { Finish(action); return; }
            try
            {
                _reveal ??= RewardFeedbackPresenter.Create(this);
                _reveal.Show(EnsureCanvas(), item, () =>
                {
                    if (IsCurrent(action)) Finish(action);
                });
                if (!IsCurrent(action)) return;
                _watchFeedback = true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[MapDestructible] 反馈不可用，返回地图：" + exception.Message);
                Finish(action);
            }
        }

        public void CloseSilently()
        {
            if (_action == null) return;
            // A synchronous strict save cannot be stopped midway by a view callback.
            // Defer cleanup until its result is known; never re-enter the rule owner.
            if (_action.Committing) { _action.CloseRequested = true; return; }
            Finish(_action);
        }

        private void Finish(MapAction action)
        {
            if (action.Finished) return;
            action.Finished = true;
            if (!ReferenceEquals(_action, action)) return;
            _action = null; _generation++; _watchFeedback = false;
            var routine = _routine; _routine = null;
            var lease = _flowLease; _flowLease = null;
            try { if (routine != null) StopCoroutine(routine); }
            catch (Exception exception) { Debug.LogWarning("[MapDestructible] 停止等待失败：" + exception.Message); }
            try { _reveal?.CloseSilently(); }
            catch (Exception exception) { Debug.LogWarning("[MapDestructible] 关闭反馈失败：" + exception.Message); }
            try { if (HasSameMap(action)) action.Presentation.SetInteractionEnabled(true); }
            catch (Exception exception) { Debug.LogWarning("[MapDestructible] 恢复地图输入失败：" + exception.Message); }
            try { lease?.Dispose(); }
            catch (Exception exception) { Debug.LogWarning("[MapDestructible] 释放流程屏障失败：" + exception.Message); }
            if (action.Saved) Publish(InteractionFinished, "saved action completion");
        }

        private static void Publish(Action listeners, string phase)
        {
            if (listeners == null) return;
            foreach (Action listener in listeners.GetInvocationList())
            {
                try { listener(); }
                catch (Exception exception)
                { Debug.LogWarning("[MapDestructible] " + phase + " callback failed: " + exception.Message); }
            }
        }

        private void LateUpdate()
        {
            var action = _action;
            if (action == null || !_watchFeedback) return;
            if (!HasSameMap(action) || _reveal == null || !_reveal.isActiveAndEnabled ||
                !_reveal.IsShown || !_reveal.Root.activeInHierarchy)
                Finish(action);
        }

        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;
            var root = new GameObject("MapDestructibleFeedbackCanvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            _canvas = root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true; _canvas.sortingOrder = 32080;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return _canvas;
        }

        private void OnDisable() => CloseSilently();
        private void OnDestroy() => CloseSilently();
    }
}
