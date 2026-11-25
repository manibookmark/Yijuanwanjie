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
    /// Resumes legacy saved mana pickups without asking the player to click again.
    /// New conversions settle mana in their original transaction. Dungeon owns
    /// legacy proof, idempotency, the mana increase and persistence rollback.
    /// </summary>
    public sealed class DungeonRunManaPlatformCoordinator : MonoBehaviour
    {
        private DungeonRunSession _run;
        private LevelRunSession _floor;
        private MapPresentation _map;
        private CombatScenePreloader _preloader;
        private Action _persist;
        private Func<bool> _canPersist;
        private IDisposable _inputLease;
        private Coroutine _routine;
        private Coroutine _resumeRoutine;
        private RewardFeedbackPresenter _feedback;
        private Canvas _canvas;
        private bool _active, _committing, _closeRequested;
        public bool IsActive => _active;
        public event Action MapStateCommitted;
        public event Action InteractionFinished;

        public void Bind(DungeonRunSession run, MapPresentation map,
            CombatScenePreloader preloader, Action persist, Func<bool> canPersist)
        {
            CloseSilently();
            _run = run; _floor = run?.CurrentFloor; _map = map;
            _preloader = preloader; _persist = persist; _canPersist = canPersist;
            ResumeLegacyPending();
        }

        public void ResumeLegacyPending()
        {
            if (_resumeRoutine == null && IsCurrent())
                _resumeRoutine = StartCoroutine(ResumePending());
        }

        private IEnumerator ResumePending()
        {
            yield return null;
            // Only old two-step saves create these entries. Take a detached list:
            // collecting one changes the journal and must never mutate enumeration.
            var pending = _run.CardConversions.GetPendingLegacyManaPickups();
            foreach (var pickup in pending)
            {
                while (IsCurrent() && (_active || !_map.IsInteractionEnabled)) yield return null;
                if (!IsCurrent()) yield break;
                if (!TryBegin(pickup.FloorContentId, pickup.NodeContentId, out string error))
                {
                    Debug.LogWarning("[ManaPlatform] 待领灵力自动接续暂不可用：" + error);
                    break; // Keep the journal and legal retry; never spin on a failed save.
                }
                while (_active && IsCurrent()) yield return null;
            }
            _resumeRoutine = null;
        }

        public bool TryBegin(string nodeId, out string error)
            => TryBegin(_run?.CurrentFloorContentId, nodeId, out error);

        private bool TryBegin(string floorId, string nodeId, out string error)
        {
            error = null;
            if (_active || !IsCurrent() || _persist == null || _canPersist?.Invoke() != true)
            { error = "聚灵台尚未绑定可保存的当前地图。"; return false; }
            if (floorId == _run.CurrentFloorContentId &&
                (!_run.CardConversions.TryInspectManaPickup(nodeId, out var offer, out error) ||
                 !offer.CanCollect)) return false;
            _active = true; _closeRequested = false;
            _map.SetInteractionEnabled(false);
            _inputLease = SceneTransitionRuntime.ExternalModalBarrier?.Hold("modal.mana-platform-pickup");
            _preloader?.InvalidatePreparedRunState();
            _routine = StartCoroutine(Collect(floorId, nodeId));
            return true;
        }

        private IEnumerator Collect(string floorId, string nodeId)
        {
            yield return null;
            while (_preloader != null && _preloader.IsBusy) yield return null;
            if (!IsCurrent()) { Finish(); yield break; }
            _run.TryGetMapStartingMana(out int before);
            LevelInteractionReceipt receipt = default;
            bool saved = false;
            string error = null;
            _committing = true;
            try
            {
                saved = _run.CardConversions.TryCollectLegacyManaPickup(floorId, nodeId, out receipt, out error, () =>
                {
                    if (!IsCurrent() || _canPersist?.Invoke() != true)
                        throw new InvalidOperationException("聚灵台拾取的保存上下文已改变。");
                    _persist();
                });
            }
            catch (Exception exception) { error = exception.Message; }
            finally { _committing = false; }
            if (!saved)
            {
                Debug.LogWarning("[ManaPlatform] 灵气尚未领取：" + error);
                Finish(); yield break;
            }
            if (_closeRequested || !IsCurrent()) { Finish(); yield break; }

            bool collected = false;
            if (floorId == _run.CurrentFloorContentId &&
                _map.TryPlayPickupCompletion(nodeId, () => collected = true))
            {
                float elapsed = 0f;
                while (!collected && elapsed < 3f)
                { elapsed += Time.unscaledDeltaTime; yield return null; }
            }
            Publish(MapStateCommitted);
            RewardFeedbackItem item = RewardFeedbackItem.FromInteractionReceipt(receipt);
            if (_run.TryGetMapStartingMana(out int after))
                item.ProgressText = $"每回合基础灵力：{before} → {after}";
            try
            {
                _feedback ??= RewardFeedbackPresenter.Create(this);
                _feedback.ShowTimed(EnsureCanvas(), item, 2f, Finish);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ManaPlatform] 灵力已领取，反馈暂不可用：" + exception.Message);
                Finish();
            }
            _routine = null;
        }

        private bool IsCurrent() => isActiveAndEnabled && gameObject.activeInHierarchy && OwnsMap();

        private bool OwnsMap() =>
            _map != null && _map.isActiveAndEnabled &&
            ReferenceEquals(_run, DungeonRunSessionStore.Current) &&
            _run != null && ReferenceEquals(_floor, _run.CurrentFloor);

        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;
            var root = new GameObject("ManaPlatformRewardCanvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            _canvas = root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true; _canvas.sortingOrder = 32000;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return _canvas;
        }

        private void Finish()
        {
            if (!_active) return;
            _active = false;
            _inputLease?.Dispose(); _inputLease = null;
            if (OwnsMap())
            {
                Publish(MapStateCommitted);
                _map.SetInteractionEnabled(true);
            }
            Publish(InteractionFinished);
        }

        private static void Publish(Action action)
        {
            try { action?.Invoke(); }
            catch (Exception exception)
            { Debug.LogWarning("[ManaPlatform] 已保存状态的地图同步暂不可用：" + exception.Message); }
        }

        public void CloseSilently()
        {
            if (_committing) { _closeRequested = true; return; }
            if (_resumeRoutine != null) { StopCoroutine(_resumeRoutine); _resumeRoutine = null; }
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }
            _feedback?.CloseSilently();
            Finish();
        }
        private void OnDisable() => CloseSilently();
        private void OnDestroy() => CloseSilently();
    }
}
