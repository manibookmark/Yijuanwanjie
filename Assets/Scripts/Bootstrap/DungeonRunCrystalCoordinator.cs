using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Map;
using WanjieScroll.UI;
using WanjieScroll.UI.Maps;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Composes one saved Crystal lifecycle with its independent presentation and
    /// natural reward panel. Dungeon alone owns clocks, hit membership and XP;
    /// UI alone owns motion/input. Every domain command uses the same strict save.
    /// Neither a closed panel nor a drained level reward completes the map node.
    /// </summary>
    public sealed class DungeonRunCrystalCoordinator : MonoBehaviour
    {
        private DungeonRunSession _run;
        private LevelRunSession _floor;
        private MapPresentation _map;
        private MapPreviewProfile _mapProfile;
        private CombatScenePreloader _preloader;
        private DungeonRunInteractionCoordinator _rewards;
        private Func<DungeonRunInteractionCatalog> _catalog;
        private Action _persist;
        private Action _persistReward;
        private Func<bool> _canPersist;
        private CrystalThrowingView _view;
        private string _nodeId;
        private IDisposable _flowLease;
        private bool _command, _faulted, _rewardVisible, _closeDeferred, _userPaused;
        private int _generation;

        public bool IsActive => _nodeId != null;
        public event Action MapStateCommitted;
        public event Action InteractionFinished;

        public void Bind(DungeonRunSession run, MapPresentation map,
            CombatScenePreloader preloader, DungeonRunInteractionCoordinator rewards,
            Func<DungeonRunInteractionCatalog> catalog, Action persist, Action persistReward,
            Func<bool> canPersist)
        {
            if (_command) throw new InvalidOperationException("Cannot rebind during a Crystal commit.");
            CloseSilently();
            _run = run; _floor = run?.CurrentFloor; _map = map;
            _mapProfile = map?.Profile;
            _preloader = preloader; _rewards = rewards; _catalog = catalog;
            _persist = persist; _persistReward = persistReward; _canPersist = canPersist;
        }

        public static bool Handles(LevelNode node) => node?.nodeType == LevelNodeType.Interaction &&
            node.completionEffects?.Count == 1 &&
            node.completionEffects[0]?.kind == LevelInteractionEffectKind.CrystalThrowing;

        public bool TryBegin(string nodeId, out string error)
        {
            error = null;
            if (IsActive) { error = "已有晶体投牌正在进行。"; return false; }
            CrystalThrowingView created = null;
            try
            {
                RequireBinding();
                if (!_map.IsInteractionEnabled || !Handles(_floor.FindNode(nodeId)))
                    throw new InvalidOperationException("当前地图不能开始晶体投牌。");
                var prefab = Resources.Load<GameObject>("UI/Map/CrystalThrowingOverlay");
                var template = prefab != null ? prefab.GetComponent<CrystalThrowingView>() : null;
                if (template == null || !template.HasRequiredBindings(out error))
                    throw new InvalidOperationException(error ?? "晶体投牌的正式面板资源缺失。");
                Sprite target = _map.Profile.FindFirstNodeByGroupId(nodeId)?.ResolveSprite();
                if (target == null) throw new InvalidOperationException("此晶体节点缺少自身的地图素材。");
                var cardArt = CaptureCardArt(nodeId);
                created = Instantiate(prefab, transform).GetComponent<CrystalThrowingView>();
                if (created == null) throw new InvalidOperationException("晶体面板实例缺少表现组件。");
                _nodeId = nodeId; _view = created; _faulted = false; _rewardVisible = false;
                _closeDeferred = false; _userPaused = false; ++_generation;
                _flowLease = SceneTransitionRuntime.ExternalModalBarrier?.Hold("modal.map.crystal");
                _map.SetInteractionEnabled(false);
                _preloader?.InvalidatePreparedRunState();
                Execute(() => Require(_run.Crystals.TryBeginSource(nodeId, Persist, out var failure), failure));
                RequireCurrent();
                var offer = Inspect();
                _view.Arrived = TryAcceptHit;
                _view.CloseRequested += OnCloseRequested;
                _view.PauseRequested += OnPauseRequested;
                _view.Initialize(target, cardArt, offer);
                PublishMap();
                if (_run.Interactions.HasPendingChoice) OpenRewards();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                // If Begin was already saved, cleanup leaves its real pending
                // checkpoint intact. A later entry resumes, never finishes it.
                if (IsActive) CloseSilently();
                else if (created != null) Destroy(created.gameObject);
                return false;
            }
        }

        private Dictionary<int, Sprite> CaptureCardArt(string nodeId)
        {
            var catalog = _catalog() ?? throw new InvalidOperationException("晶体卡牌内容缺失。");
            if (!_run.TryGetCurrentCards(catalog.Cards, out var cards, out string error))
                throw new InvalidOperationException(error);
            var result = new Dictionary<int, Sprite>();
            // Portable Run authors intentionally exclude Unity object references.
            // Use the same stable-ID artwork authority as normal card views.
            var presentation = Resources.Load<CardPresentationProfile>(CardPresentationProfile.DefaultResourcePath);
            HashSet<int> frozen = null;
            if (_run.Crystals.TryInspect(nodeId, out var prior, out _))
                frozen = new HashSet<int>(prior.FrozenRosterInstanceIds);
            try
            {
                foreach (var card in cards)
                {
                    if (card != null && frozen != null && !frozen.Contains(card.runtimeInstanceId)) continue;
                    Sprite artwork = card?.cardArt ?? presentation?.GetArtwork(card?.contentId);
                    if (card == null || card.runtimeInstanceId <= 0 || artwork == null)
                        throw new InvalidOperationException("晶体投牌缺少卡牌实例或统一插图：" + (card?.contentId ?? "<null>"));
                    result.Add(card.runtimeInstanceId, artwork);
                }
                if (frozen != null && result.Count != frozen.Count)
                    throw new InvalidOperationException("晶体冻结队列的卡牌实例或插图缺失。");
                return result;
            }
            finally { foreach (var card in cards) if (card != null) Destroy(card); }
        }

        private void Update()
        {
            if (!IsActive || _command || _faulted || _view == null) return;
            try
            {
                RequireCurrent();
                if (_run.Interactions.HasPendingChoice)
                { OpenRewards(); return; }
                if (_rewardVisible) return;
                bool paused = _userPaused || !Application.isFocused || Time.timeScale <= 0f ||
                    (_preloader != null && _preloader.IsBusy);
                _view.SetPaused(paused);
                if (paused) return;
                var before = Inspect();
                if (before.Stage == CrystalThrowingStage.Preparing)
                {
                    Execute(() => Require(_run.Crystals.TryStart(_nodeId, Persist, out var failure), failure));
                    _view.ApplyOffer(Inspect());
                    return;
                }
                _view.ReadInput(out bool pressed, out bool released, out bool resettingSavedHold);
                // Release a restored hold before any elapsed auto-throw time.
                float frameDelta = resettingSavedHold ? 0f : Time.deltaTime;
                IReadOnlyList<CrystalCardThrow> thrown = Array.Empty<CrystalCardThrow>();
                Execute(() => Require(_run.Crystals.TryAdvanceFrame(_nodeId, frameDelta,
                    false, pressed, released, false, Persist, out thrown, out var failure), failure));
                RequireCurrent();
                var after = Inspect();
                _view.ApplyOffer(after);
                _view.PresentThrows(thrown);
                _view.AdvancePresentation(frameDelta, false);
                if (!IsActive || _faulted || _rewardVisible) return;
                after = Inspect();
                if (after.Stage == CrystalThrowingStage.Ending && after.ExitElapsed >= after.ExitDuration)
                    Finish();
            }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private bool TryAcceptHit(int physicalId)
        {
            if (!IsActive || _faulted || _rewardVisible || _userPaused || _command ||
                !Application.isFocused || Time.timeScale <= 0f) return false;
            bool accepted = false;
            try
            {
                RequireCurrent();
                if (_run.Interactions.HasPendingChoice) return false;
                var offer = Inspect();
                if (offer.Stage != CrystalThrowingStage.Throwing) return false;
                Execute(() => Require(_run.Crystals.TryHit(_nodeId, physicalId, _catalog(), Persist,
                    out _, out var failure), failure));
                accepted = true;
                RequireCurrent();
                _view.ApplyOffer(Inspect());
                PublishMap();
                if (_run.Interactions.HasPendingChoice) OpenRewards();
                return true;
            }
            catch (Exception exception) { ShowProblem(exception.Message); return accepted; }
        }

        private void OpenRewards()
        {
            if (_rewardVisible || !IsActive) return;
            RequireCurrent();
            _view.SetPaused(true);
            _rewardVisible = true;
            int generation = _generation;
            if (!_rewards.TryResumeCrystalRewards(_nodeId, PersistReward, () =>
            {
                if (!IsActive || generation != _generation) return;
                _rewardVisible = false;
                try { RequireCurrent(); _view.ApplyOffer(Inspect()); _view.SetPaused(false); }
                catch (Exception exception) { ShowProblem(exception.Message); }
            }, out string error))
            { _rewardVisible = false; ShowProblem(error); }
        }

        private void Finish()
        {
            Execute(() => Require(_run.Crystals.TryFinish(_nodeId, Persist, out _, out var failure), failure));
            PublishMap();
            CloseSilently();
            PublishFinished();
        }

        private CrystalRunOffer Inspect()
        {
            Require(_run.Crystals.TryInspect(_nodeId, out var offer, out string error), error);
            return offer;
        }

        private void Persist()
        {
            RequireCurrent();
            _persist();
            // Returning from the atomic save means publication. Presentation
            // teardown after that point must not roll back only the memory copy.
        }

        private void PersistReward()
        {
            RequireCurrent();
            _persistReward();
        }

        private void Execute(Action command)
        {
            if (_command) throw new InvalidOperationException("Crystal presentation command reentry.");
            _command = true;
            try { command(); }
            finally
            {
                _command = false;
                if (_closeDeferred) CloseSilently();
            }
        }

        private void RequireBinding()
        {
            if (_run == null || _floor == null || !HasCurrentMap() || _catalog == null ||
                _rewards == null || _persist == null || _persistReward == null || _canPersist == null || !_canPersist() ||
                !isActiveAndEnabled || !gameObject.activeInHierarchy ||
                !ReferenceEquals(_run, DungeonRunSessionStore.Current) ||
                !ReferenceEquals(_floor, _run.CurrentFloor))
                throw new InvalidOperationException("晶体投牌需要当前地图的正式可保存 Run。");
        }

        private bool HasCurrentMap() => _map != null && _map.isActiveAndEnabled && _map.IsBuilt &&
            _mapProfile != null && ReferenceEquals(_map.Profile, _mapProfile) &&
            ReferenceEquals(_run, DungeonRunSessionStore.Current) &&
            ReferenceEquals(_floor, _run?.CurrentFloor);

        private void RequireCurrent()
        {
            RequireBinding();
            if (!IsActive || _view == null || !Handles(_floor.FindNode(_nodeId)))
                throw new InvalidOperationException("晶体投牌的当前地图绑定已改变。");
        }

        private static void Require(bool result, string error)
        { if (!result) throw new InvalidOperationException(error ?? "晶体动作没有成功提交。"); }

        private void ShowProblem(string message)
        {
            if (!IsActive || _view == null) return;
            _faulted = true;
            _view.SetPaused(true);
            Debug.LogWarning("[Crystal] 投牌暂停：" + message);
            _view.ShowProblem(message, () =>
            {
                if (!IsActive || _command) return;
                try
                {
                    RequireCurrent(); _view.ApplyOffer(Inspect()); _faulted = false;
                    _view.SetPaused(false);
                    if (_run.Interactions.HasPendingChoice) OpenRewards();
                }
                catch (Exception exception) { ShowProblem(exception.Message); }
            });
        }

        private void OnCloseRequested()
        {
            // A leave request cannot turn an unfinished event or a natural
            // reward panel into completion. Runtime teardown uses CloseSilently.
            if (!IsActive || _command || _rewardVisible) return;
            if (Inspect().Stage == CrystalThrowingStage.Returned) CloseSilently();
        }

        private void OnPauseRequested()
        {
            if (!IsActive || _command || _faulted || _rewardVisible) return;
            _userPaused = !_userPaused;
            _view.SetPaused(_userPaused || !Application.isFocused || Time.timeScale <= 0f);
        }

        public void CloseSilently()
        {
            if (_command) { _closeDeferred = true; return; }
            bool ownedMap = IsActive;
            ++_generation;
            try { if (_rewardVisible) _rewards?.CloseSilently(); }
            catch (Exception exception) { Debug.LogWarning("[Crystal] 奖励面板关闭失败：" + exception.Message); }
            if (_view != null)
            {
                _view.CloseRequested -= OnCloseRequested;
                _view.PauseRequested -= OnPauseRequested;
                _view.Arrived = null;
                try { _view.Close(); }
                catch (Exception exception) { Debug.LogWarning("[Crystal] 投牌面板关闭失败：" + exception.Message); }
                Destroy(_view.gameObject);
            }
            _view = null; _nodeId = null; _faulted = false;
            _rewardVisible = false; _closeDeferred = false; _userPaused = false;
            IDisposable lease = _flowLease; _flowLease = null;
            try { lease?.Dispose(); }
            catch (Exception exception) { Debug.LogWarning("[Crystal] 地图模态释放失败：" + exception.Message); }
            if (ownedMap && HasCurrentMap()) _map.SetInteractionEnabled(true);
        }

        // Presentation notifications occur after publication and cannot make a
        // successfully saved hit look rejected to the flying-card consumer.
        private void PublishMap()
        {
            try { MapStateCommitted?.Invoke(); }
            catch (Exception exception) { Debug.LogWarning("[Crystal] 已保存，地图反馈失败：" + exception.Message); }
        }
        private void PublishFinished()
        {
            try { InteractionFinished?.Invoke(); }
            catch (Exception exception) { Debug.LogWarning("[Crystal] 已完成，地图反馈失败：" + exception.Message); }
        }
        private void OnDisable() => CloseSilently();
        private void OnDestroy() => CloseSilently();
    }
}
