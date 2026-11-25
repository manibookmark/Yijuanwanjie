using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Map;
using WanjieScroll.Effects.Transitions;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    public delegate bool LevelFloorExitCommit(
        string nodeContentId,
        out DungeonRunFloorExitResult result,
        out string error);

    /// <summary>
    /// 地图进入战斗协调器。接收节点点击并发起进入战斗的统一转场。
    /// 非战斗交互节点不走战斗转场，由类型化效果执行器结算并展示反馈。
    /// 规模审查：本类继续拥有节点进入/完成生命周期；新增 Run 选择的面板、
    /// 候选和事务推进交给各自选择协调器，不在此扩展规则。Arcana/Offering 的严格
    /// 保存包含节点完成，本类只消费回执。神像的逐次输入/反馈生命周期由独立
    /// 协调器拥有，在普通 Begin 前分流；本类不持有神像进度、余额或完成状态。
    /// 献卡属性选择继续由独立协调器持有面板生命周期，Dungeon持有候选与结算；
    /// 此处仅分派进入、消费耐久回执并恢复地图，不增加候选或属性规则。
    /// 拓印/请灵通过同一设施生命周期合同注册；本类只路由、等待和恢复，
    /// 不持有复制策略、英灵候选、支付或演出状态。下次新增独立流程前重新审查。
    /// 入战预反馈只路由 Core 的请求所有权与 Effects 遮幕就绪，不持有雾的视觉状态。
    /// </summary>
    public sealed class LevelMapEntryCoordinator : MonoBehaviour
    {
        private LevelRunSession _session;
        private MapPresentation _presentation;
        private SceneTransitionProfile _transitionProfile;
        private CombatScenePreloader _combatPreloader;
        private LevelInteractionEffectExecutor _interactionEffectExecutor;
        private bool _entering;
        private Coroutine _pendingEntry;
        private SceneTransitionRuntimeBootstrap _combatPreparationRuntime;
        private string _combatPreparationRequestId;
        private SceneTransitionRuntimeBootstrap _transitionRuntime;
        private string _combatTransitionRequestId;
        private DungeonRunSession _combatTransitionRun;
        private Canvas _rewardCanvas;
        private RewardFeedbackPresenter _rewardRevealOverlay;
        private CharacterPresentationCoordinator _characterPresenter;
        private SealAssignmentCoordinator _sealAssignmentCoordinator;
        private RunCardConversionCoordinator _cardConversionCoordinator;
        private DungeonRunInteractionCoordinator _dungeonInteractionCoordinator;
        private DungeonRunArcanaCoordinator _arcanaCoordinator;
        private DungeonRunChickenOfferingCoordinator _chickenOfferingCoordinator;
        private DungeonRunCardStatOfferingCoordinator _cardStatOfferingCoordinator;
        private DungeonRunManaStatueCoordinator _manaStatueCoordinator;
        private DungeonRunManaPlatformCoordinator _manaPlatformCoordinator;
        private DungeonRunCrystalCoordinator _crystalCoordinator;
        private IReadOnlyList<IDungeonMapCardFacility> _cardFacilities = Array.Empty<IDungeonMapCardFacility>();
        private LevelFloorExitCommit _commitFloorExit;
        // Presentation-only acknowledgement: gameplay is already saved in Dungeon.
        private string _committedExitFeedbackNodeId;

        /// <summary>是否正在进入战斗或收集奖励。</summary>
        public bool IsEntering => _entering || (_manaStatueCoordinator != null && _manaStatueCoordinator.IsActive) ||
            (_manaPlatformCoordinator != null && _manaPlatformCoordinator.IsActive) ||
            (_crystalCoordinator != null && _crystalCoordinator.IsActive);

        /// <summary>奖励收集完成后触发，供 Bootstrap 启动下一场预载。</summary>
        public event Action<IReadOnlyList<LevelInteractionReceipt>>
            InteractionCompleted;
        public event Action<LevelEncounterLaunch> EncounterStarted;
        public event Action<string> InteractionStarted;
        public event Action<string> InteractionEffectsCommitted;
        /// <summary>Dungeon already saved the reward; subscribers only refresh projections.</summary>
        public event Action DurableInteractionCompleted;
        public event Action<string> InteractionCancelled;
        public event Action<string> ExitCompleted;

        /// <summary>
        /// 绑定依赖。
        /// </summary>
        public void Bind(
            LevelRunSession session,
            MapPresentation presentation,
            SceneTransitionProfile transitionProfile,
            CombatScenePreloader combatPreloader = null,
            LevelInteractionEffectExecutor interactionEffectExecutor = null,
            CharacterPresentationCoordinator characterPresenter = null,
            SealAssignmentCoordinator sealAssignmentCoordinator = null,
            RunCardConversionCoordinator cardConversionCoordinator = null,
            DungeonRunInteractionCoordinator dungeonInteractionCoordinator = null,
            LevelFloorExitCommit commitFloorExit = null)
        {
            _session = session;
            _presentation = presentation;
            _transitionProfile = transitionProfile;
            _combatPreloader = combatPreloader;
            _interactionEffectExecutor = interactionEffectExecutor;
            _characterPresenter = characterPresenter;
            _sealAssignmentCoordinator = sealAssignmentCoordinator;
            _cardConversionCoordinator = cardConversionCoordinator;
            _dungeonInteractionCoordinator = dungeonInteractionCoordinator;
            _commitFloorExit = commitFloorExit;
        }

        public void BindArcana(DungeonRunArcanaCoordinator coordinator)
        {
            _arcanaCoordinator?.CloseSilently();
            _arcanaCoordinator = coordinator;
        }

        public void BindChickenOffering(DungeonRunChickenOfferingCoordinator coordinator)
        {
            _chickenOfferingCoordinator?.CloseSilently();
            _chickenOfferingCoordinator = coordinator;
        }

        public void BindCardStatOffering(DungeonRunCardStatOfferingCoordinator coordinator)
        {
            _cardStatOfferingCoordinator?.CloseSilently();
            _cardStatOfferingCoordinator = coordinator;
        }

        public void BindCardFacilities(params IDungeonMapCardFacility[] facilities)
        {
            foreach (var facility in _cardFacilities) facility?.CloseSilently();
            _cardFacilities = facilities ?? Array.Empty<IDungeonMapCardFacility>();
        }

        private IDungeonMapCardFacility FindCardFacility(LevelNode node)
        {
            if (node?.completionEffects?.Count != 1) return null;
            foreach (var facility in _cardFacilities)
                if (facility != null && (node.completionEffects[0]?.kind == facility.EffectKind ||
                    node.completionEffects[0]?.kind == LevelInteractionEffectKind.GunForHire &&
                    facility.EffectKind == LevelInteractionEffectKind.CrawlerInvitation)) return facility;
            return null;
        }

        private void CloseCardFacilities()
        { foreach (var facility in _cardFacilities) facility?.CloseSilently(); }

        public void BindManaStatue(DungeonRunManaStatueCoordinator coordinator)
        {
            _manaStatueCoordinator?.CloseSilently();
            _manaStatueCoordinator = coordinator;
        }

        public void BindManaPlatform(DungeonRunManaPlatformCoordinator coordinator)
        {
            _manaPlatformCoordinator?.CloseSilently();
            _manaPlatformCoordinator = coordinator;
        }


        public void BindCrystal(DungeonRunCrystalCoordinator coordinator)
        {
            _crystalCoordinator?.CloseSilently();
            _crystalCoordinator = coordinator;
        }

        /// <summary>
        /// 尝试进入指定节点的遭遇或收集奖励。
        /// </summary>
        public bool TryEnterEncounter(string nodeContentId)
        {
            if (IsEntering) return false;
            if (_session == null || _presentation == null) return false;

            var node = _session.FindNode(nodeContentId);
            if (node == null) return false;

            // 任何预载/转场/节点状态变更之前，先通过 Run 的公共只读准入。
            // 待选节点可恢复；不能用返回地图绕过同一 Run 尚未完成的奖励。
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run != null)
            {
                if (!ReferenceEquals(run.CurrentFloor, _session)) return false;
                DungeonRunNodeAdmission admission = run.GetNodeAdmission(nodeContentId);
                if (admission != DungeonRunNodeAdmission.Allowed &&
                    admission != DungeonRunNodeAdmission.ResumePendingChoice)
                {
                    Debug.LogWarning($"[LevelMapEntryCoordinator] Run 准入拒绝 {nodeContentId}：{admission}");
                    return false;
                }
            }

            if (run != null && run.CardConversions.HasPendingManaPickup(
                    run.CurrentFloorContentId, nodeContentId))
            {
                if (_manaPlatformCoordinator == null) return false;
                bool started = _manaPlatformCoordinator.TryBegin(nodeContentId, out string pickupError);
                if (!started) Debug.LogWarning("[LevelMapEntryCoordinator] 灵气暂不可领取：" + pickupError);
                return started;
            }

            // Each destructible click is one explicit owner action. The Cart
            // pickup remains independently actionable after its node is cleared.
            if (DungeonRunManaStatueCoordinator.Handles(node))
            {
                if (_manaStatueCoordinator == null)
                {
                    Debug.LogWarning("[LevelMapEntryCoordinator] 破坏物的地图协调器尚未绑定。");
                    return false;
                }
                bool started = _manaStatueCoordinator.TryBegin(nodeContentId, out string statueError);
                if (!started)
                    Debug.LogWarning("[LevelMapEntryCoordinator] 破坏物暂不可操作：" + statueError);
                return started;
            }

            // Crystal stages its own node activation and frozen roster in the
            // same strict save. Do not let the generic executor begin/finish it.
            if (DungeonRunCrystalCoordinator.Handles(node))
            {
                if (_crystalCoordinator == null) return false;
                bool started = _crystalCoordinator.TryBegin(nodeContentId, out string crystalError);
                if (!started) Debug.LogWarning("[LevelMapEntryCoordinator] 晶体暂不可操作：" + crystalError);
                return started;
            }

            // Close map interaction immediately
            _presentation.SetInteractionEnabled(false);

            // 非战斗交互：直接结算并展示获得窗，不走战斗转场。
            if (node.nodeType == LevelNodeType.Exit &&
                !DungeonRunSessionStore.CanAdvanceCurrentFloor(
                    out string advanceError))
            {
                Debug.LogWarning(
                    $"[LevelMapEntryCoordinator] 暂时无法离层 " +
                    $"{nodeContentId}：{advanceError}");
                _entering = true;
                _rewardRevealOverlay ??= RewardFeedbackPresenter.Create(this);
                _rewardRevealOverlay.Show(EnsureRewardCanvas(), new RewardFeedbackItem
                {
                    Kind = RewardFeedbackKind.Unavailable,
                    Title = "暂时无法离层",
                    DisplayName = "暂不可前往下一层",
                    DecisionHint = "暂不可前往下一层。\n本层进度已保留。",
                    Description = "前往下一层的条件尚未满足，本层进度已保留。"
                }, () =>
                {
                    _entering = false;
                    if (_presentation != null)
                        _presentation.SetInteractionEnabled(true);
                });
                return false;
            }

            if (node.nodeType == LevelNodeType.Interaction ||
                node.nodeType == LevelNodeType.Exit)
            {
                return TryBeginInteraction(nodeContentId);
            }

            _entering = true;
            _combatPreparationRuntime = SceneTransitionRuntimeBootstrap.Ensure();
            _combatPreparationRequestId = Guid.NewGuid().ToString("N");
            var mapProfile = _presentation.Profile;
            if (!_combatPreparationRuntime.TryBeginBattlePreparation(
                    _combatPreparationRequestId, mapProfile != null ? mapProfile.battleBackground : null,
                    mapProfile != null ? mapProfile.battleBackgroundTint : Color.white, out string feedbackError))
            {
                Debug.LogWarning("[LevelMapEntryCoordinator] 入战反馈暂不可用：" + feedbackError);
                ReleaseCombatLoading();
                _entering = false;
                return false;
            }
            _pendingEntry = StartCoroutine(PrepareCombatAfterLoadingFeedback(
                nodeContentId, _combatPreparationRequestId));
            return true;
        }

        private IEnumerator PrepareCombatAfterLoadingFeedback(string nodeContentId, string requestId)
        {
            // Open the map onto an opaque cached background before checkpoints,
            // persistence and actor construction can occupy the main thread.
            // One frame is enough: BattlePreparationMistView has already made
            // its preview opaque and raycast-blocking. Waiting for the paper
            // panels to finish here would serialize the opening with the hidden
            // combat build and recreate the visible pause this transition exists
            // to remove. SceneTransitionController still holds the opaque cover
            // until the opening and destination are both ready.
            yield return null;
            _pendingEntry = null;
            bool accepted = false;
            try
            {
                if (_combatPreparationRuntime != null &&
                    _combatPreparationRuntime.OwnsBattlePreparation(requestId))
                    accepted = TryPrepareCombatEntry(nodeContentId, requestId);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RecoverInterruptedEncounter();
            }
            if (!accepted)
            {
                ReleaseCombatLoading();
                _entering = false;
                _presentation?.SetInteractionEnabled(true);
            }
        }

        private bool TryPrepareCombatEntry(string nodeContentId, string requestId)
        {
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run != null && !ReferenceEquals(run.CurrentFloor, _session)) return false;
            // 点击是最终、类型化的目标确认。即使 Hover 未发生（触屏、脚本调用），
            // 也必须在会话把节点推进到 Completing 之前建立战斗回滚边界。
            // 预载和 Combat 构建可能触发一次活动 Run 保存，因此检查点必须
            // 早于 Prepare；否则预载过程会把半场战斗写进存档。
            bool combatCheckpointOpened = false;
            if (run != null)
            {
                if (!DungeonRunSessionStore.TryBeginCombatRollbackCheckpoint(
                        run,
                        nodeContentId,
                        out string checkpointError))
                {
                    Debug.LogWarning(
                        $"[LevelMapEntryCoordinator] 无法建立战斗回滚边界 " +
                        $"{nodeContentId}：{checkpointError}");
                    _presentation.SetInteractionEnabled(true);
                    return false;
                }
                combatCheckpointOpened = true;
            }

            bool residentEntry = run != null && FloorCombatSceneOwner.IsPilot(_session);
            if (residentEntry)
            {
                if (FloorCombatSceneOwner.Current == null ||
                    !FloorCombatSceneOwner.Current.PrepareEncounter(_session, nodeContentId))
                {
                    if (combatCheckpointOpened) DungeonRunSessionStore.DiscardCombatRollbackCheckpoint();
                    Debug.LogWarning("[LevelMapEntryCoordinator] 楼层战斗框架暂未就绪。");
                    _presentation.SetInteractionEnabled(true);
                    return false;
                }
            }
            else
                _combatPreloader?.Prepare(_session, nodeContentId);

            if (!_session.TryBeginEncounter(
                    nodeContentId,
                    out LevelEncounterLaunch launch,
                    out string error))
            {
                if (combatCheckpointOpened)
                    DungeonRunSessionStore.DiscardCombatRollbackCheckpoint();
                FloorCombatSceneOwner.Current?.DiscardPreparedEncounter(_session);
                Debug.LogWarning(
                    $"[LevelMapEntryCoordinator] 无法开始遭遇 {nodeContentId}：{error}");
                _presentation.SetInteractionEnabled(true);
                return false;
            }

            _entering = true;
            EncounterStarted?.Invoke(launch);

            // 进入战斗时强制停止卷灵演出和语音，避免残留到战斗中。
            _characterPresenter?.CloseSilently();

            // Build context
            var context = new SceneContextBuilder()
                .Set(SceneContextKeys.EncounterNodeContentId, launch.NodeContentId)
                .Set(SceneContextKeys.EncounterRunId, launch.RunId)
                .Set(SceneContextKeys.EncounterScenarioId, launch.ScenarioId)
                .Set(SceneContextKeys.TransitionRequestId, requestId);

            // 遮幕先行：预载仍在构建、或旧 Combat 仍在清理时也立即起转场。
            // 隐藏装配是一帧数百毫秒的同步工作；先等它收敛再拉遮幕，玩家会在
            // 可见地图上白等。加载句柄本身已承担串行化：预载中由
            // PreloadedCombatSceneLoadHandle 等 Ready，清理中由
            // CleanupThenCombatSceneLoadHandle 先卸载再加载，遮幕因此盖住重活。
            if (_combatPreloader != null && _combatPreloader.IsBusy)
            {
                Debug.Log(
                    "[LevelMapEntryCoordinator] 后台战斗仍在准备，先起遮幕再等预载收敛。");
            }

            BeginTransition(requestId, context);

            return true;
        }

        private bool TryBeginInteraction(string nodeContentId)
        {
            LevelNode node = _session.FindNode(nodeContentId);
            bool cardStat = DungeonRunCardStatOfferingCoordinator.Handles(node);
            var facility = FindCardFacility(node);
            bool offering = DungeonRunChickenOfferingCoordinator.Handles(node) || cardStat || facility != null;
            if (offering && (facility != null ? !facility.CanBegin : cardStat
                    ? _cardStatOfferingCoordinator == null || !_cardStatOfferingCoordinator.CanBegin
                    : _chickenOfferingCoordinator == null || !_chickenOfferingCoordinator.CanBegin))
            {
                Debug.LogWarning("[LevelMapEntryCoordinator] 供奉需要正式存档和已绑定的选择协调器。");
                _presentation.SetInteractionEnabled(true);
                return false;
            }
            // Offering's owner stages node entry and candidate freeze in one save.
            if (!offering && !_session.TryBeginInteraction(nodeContentId, out string error))
            {
                Debug.LogWarning(
                    $"[LevelMapEntryCoordinator] 无法开始奖励收集 {nodeContentId}：{error}");
                _presentation.SetInteractionEnabled(true);
                return false;
            }

            if (_interactionEffectExecutor == null)
            {
                Debug.LogError(
                    "[LevelMapEntryCoordinator] 缺少地图交互效果执行器。");
                _session.RecoverInterruptedEncounter();
                _presentation.SetInteractionEnabled(true);
                return false;
            }

            _entering = true;
            if (!offering) InteractionStarted?.Invoke(nodeContentId);
            _pendingEntry = StartCoroutine(PlayInteractionGuarded(nodeContentId));
            return true;
        }

        private IEnumerator PlayInteractionGuarded(string nodeContentId)
        {
            IEnumerator steps = PlayInteraction(nodeContentId);
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try
                    {
                        more = steps.MoveNext();
                        if (more) current = steps.Current;
                    }
                    catch (Exception exception) when (_committedExitFeedbackNodeId == nodeContentId)
                    {
                        Debug.LogWarning("[LevelMapEntryCoordinator] 离层已保存，跳过失败的反馈并进入已提交的新层：" + exception.Message);
                        CompleteCommittedExitFeedback(nodeContentId);
                        more = false;
                    }
                    catch (Exception exception) when (TryGetCommittedChickenReceipt(nodeContentId, out _))
                    {
                        Debug.LogWarning("[LevelMapEntryCoordinator] 供奉已保存，继续恢复地图：" + exception.Message);
                        CompleteCommittedChickenFeedback(nodeContentId);
                        more = false;
                    }
                    catch (Exception exception) when (TryGetCommittedCardStatReceipt(nodeContentId, out _))
                    {
                        Debug.LogWarning("[LevelMapEntryCoordinator] 献卡已保存，继续恢复地图：" + exception.Message);
                        CompleteCommittedCardStatFeedback(nodeContentId);
                        more = false;
                    }
                    catch (Exception exception) when (TryGetCommittedCardFacilityReceipt(nodeContentId, out _))
                    {
                        Debug.LogWarning("[LevelMapEntryCoordinator] 设施收益已保存，恢复地图：" + exception.Message);
                        CompleteCommittedCardFacilityFeedback(nodeContentId);
                        more = false;
                    }
                    catch (Exception exception) when (TryGetCommittedArcanaReceipt(nodeContentId, out _))
                    {
                        Debug.LogWarning("[LevelMapEntryCoordinator] 天机已保存，跳过失败的展示并恢复地图：" + exception.Message);
                        CompleteCommittedArcanaFeedback(nodeContentId);
                        more = false;
                    }
                    catch (Exception exception) when (HasCommittedManaConversion(nodeContentId))
                    {
                        Debug.LogWarning("[LevelMapEntryCoordinator] 聚灵已保存，恢复地图：" + exception.Message);
                        _cardConversionCoordinator?.CloseSilently();
                        _rewardRevealOverlay?.CloseSilently();
                        _entering = false; _pendingEntry = null;
                        SyncNodeStatesWithSession();
                        _presentation?.SetInteractionEnabled(true);
                        DurableInteractionCompleted?.Invoke();
                        more = false;
                    }
                    if (!more) yield break;
                    yield return current;
                }
            }
            finally { (steps as IDisposable)?.Dispose(); }
        }

        private IEnumerator PlayInteraction(string nodeContentId)
        {
            LevelNode node = _session.FindNode(nodeContentId);
            int? startingManaBefore = null;
            DungeonRunSession feedbackRun = DungeonRunSessionStore.Current;
            if (feedbackRun != null && feedbackRun.TryGetMapStartingMana(out int startingMana))
                startingManaBefore = startingMana;
            LevelInteractionExecutionContext executionContext = default;
            IReadOnlyList<LevelInteractionReceipt> receipts = null;
            bool effectsExecuted = false;
            bool floorExit = node?.nodeType == LevelNodeType.Exit;
            bool runInteraction = DungeonRunInteractionCoordinator.Handles(node);
            bool arcanaInteraction = DungeonRunArcanaCoordinator.Handles(node);
            bool chickenInteraction = DungeonRunChickenOfferingCoordinator.Handles(node);
            bool cardStatInteraction = DungeonRunCardStatOfferingCoordinator.Handles(node);
            var cardFacility = FindCardFacility(node);
            bool cardFacilityInteraction = cardFacility != null;
            bool gemInteraction = node?.completionEffects?.Count == 1 && node.completionEffects[0]?.kind == LevelInteractionEffectKind.GemChest;
            // Any map interaction may change the captured next-battle state,
            // including ConvertCard, direct seals, health and base-stat rewards.
            // Discard and await cleanup before exposing a commit path; do not
            // infer cache validity from which UI coordinator handles the node.
            _combatPreloader?.InvalidatePreparedRunState();
            while (_combatPreloader != null && _combatPreloader.IsBusy)
                yield return null;

            if (floorExit)
            {
                // Dungeon owns payout + floor replacement + persistence as one
                // transaction. Presentation only consumes its frozen receipt;
                // it must never pay again or separately advance after feedback.
                string exitError = null;
                DungeonRunFloorExitResult exitResult = null;
                if (_commitFloorExit == null ||
                    !_commitFloorExit(nodeContentId, out exitResult, out exitError))
                {
                    AbortInteraction(nodeContentId,
                        exitError ?? "缺少统一离层事务。");
                    yield break;
                }
                receipts = exitResult.Receipts;
                effectsExecuted = true;
                _committedExitFeedbackNodeId = nodeContentId;
            }
            else if (cardFacilityInteraction)
            {
                bool resolved = false, cancelled = false;
                LevelInteractionReceipt committed = default;
                if (!cardFacility.TryBegin(nodeContentId,
                        receipt => { committed = receipt; resolved = true; },
                        () => { cancelled = true; resolved = true; }, out var beginError))
                { AbortInteraction(nodeContentId, beginError); yield break; }
                while (!resolved)
                {
                    if (!cardFacility.IsActive)
                    {
                        cancelled = !TryGetCommittedCardFacilityReceipt(nodeContentId, out committed);
                        resolved = true; break;
                    }
                    yield return null;
                }
                if (cancelled) { CancelInteraction(nodeContentId); yield break; }
                receipts = new[] { committed }; effectsExecuted = true;
            }
            else if (cardStatInteraction)
            {
                bool resolved = false, cancelled = false;
                LevelInteractionReceipt committed = default;
                string beginError = null;
                if (_cardStatOfferingCoordinator == null || !_cardStatOfferingCoordinator.TryBegin(nodeContentId,
                    receipt => { committed = receipt; resolved = true; },
                    () => { cancelled = true; resolved = true; }, out beginError))
                { AbortInteraction(nodeContentId, beginError ?? "献卡选择尚未准备好。"); yield break; }
                while (!resolved)
                {
                    if (_cardStatOfferingCoordinator == null || !_cardStatOfferingCoordinator.IsActive)
                    {
                        cancelled = !TryGetCommittedCardStatReceipt(nodeContentId, out committed);
                        resolved = true; break;
                    }
                    yield return null;
                }
                if (cancelled) { CancelInteraction(nodeContentId); yield break; }
                receipts = new[] { committed }; effectsExecuted = true;
            }
            else if (chickenInteraction)
            {
                bool resolved = false, cancelled = false;
                LevelInteractionReceipt committed = default;
                string beginError = null;
                if (_chickenOfferingCoordinator == null || !_chickenOfferingCoordinator.TryBegin(nodeContentId,
                    receipt => { committed = receipt; resolved = true; },
                    () => { cancelled = true; resolved = true; }, out beginError))
                { AbortInteraction(nodeContentId, beginError ?? "供奉选择尚未准备好。"); yield break; }
                while (!resolved)
                {
                    if (_chickenOfferingCoordinator == null || !_chickenOfferingCoordinator.IsActive)
                    {
                        cancelled = !TryGetCommittedChickenReceipt(nodeContentId, out committed);
                        resolved = true; break;
                    }
                    yield return null;
                }
                if (cancelled) { CancelInteraction(nodeContentId); yield break; }
                receipts = new[] { committed }; effectsExecuted = true;
            }
            else if (arcanaInteraction)
            {
                bool resolved = false, cancelled = false;
                LevelInteractionReceipt committed = default;
                string beginError = null;
                if (_arcanaCoordinator == null || !_arcanaCoordinator.TryBegin(nodeContentId,
                        receipt => { committed = receipt; resolved = true; },
                        () => { cancelled = true; resolved = true; }, out beginError))
                {
                    AbortInteraction(nodeContentId, beginError ?? "缺少天机选择协调器。");
                    yield break;
                }
                while (!resolved)
                {
                    if (_arcanaCoordinator == null || !_arcanaCoordinator.IsActive)
                    {
                        cancelled = !TryGetCommittedArcanaReceipt(nodeContentId, out committed);
                        resolved = true;
                        break;
                    }
                    yield return null;
                }
                if (cancelled) { CancelInteraction(nodeContentId); yield break; }
                receipts = new[] { committed };
                effectsExecuted = true;
            }
            else if (runInteraction)
            {
                bool resolved = false;
                bool cancelled = false;
                DungeonRunInteractionOffer committed = default;
                string beginError = null;
                if (_dungeonInteractionCoordinator == null ||
                    !_dungeonInteractionCoordinator.TryBegin(
                        nodeContentId,
                        offer => { committed = offer; resolved = true; },
                        () => { cancelled = true; resolved = true; },
                        out beginError))
                {
                    AbortInteraction(nodeContentId,
                        beginError ?? "缺少 Run 交互协调器。");
                    yield break;
                }
                while (!resolved)
                    yield return null;
                if (cancelled)
                {
                    CancelInteraction(nodeContentId);
                    yield break;
                }
                receipts = new[] { committed.Receipt };
                effectsExecuted = true;
            }
            SealDispositionSelection sealSelection = default;
            LevelInteractionEffect sealEffect = runInteraction || arcanaInteraction || chickenInteraction || cardStatInteraction || cardFacilityInteraction || floorExit
                ? null : FindPendingSealEffect(node);
            LevelInteractionEffect conversionEffect = runInteraction || arcanaInteraction || chickenInteraction || cardStatInteraction || cardFacilityInteraction || floorExit
                ? null : FindPendingConversionEffect(node);
            bool manaConversion = false;
            if (sealEffect != null && conversionEffect != null)
            {
                AbortInteraction(
                    nodeContentId,
                    "同一节点不能同时等待钤印和卡牌归元选择。");
                yield break;
            }
            if (sealEffect != null)
            {
                if (_sealAssignmentCoordinator == null)
                {
                    AbortInteraction(
                        nodeContentId,
                        "缺少钤印选择协调器。");
                    yield break;
                }
                bool choiceResolved = false;
                if (!_sealAssignmentCoordinator.TryBegin(
                        sealEffect,
                        selected =>
                        {
                            sealSelection = selected;
                            choiceResolved = true;
                        },
                        out string beginError))
                {
                    AbortInteraction(nodeContentId, beginError);
                    yield break;
                }
                while (!choiceResolved)
                    yield return null;
                executionContext = sealSelection.IsCashOut
                    ? new LevelInteractionExecutionContext(
                        sealEffect.effectId,
                        0,
                        useUntargetedHandler: true)
                    : new LevelInteractionExecutionContext(
                        sealEffect.effectId,
                        sealSelection.CardRuntimeInstanceId);
            }
            else if (conversionEffect != null)
            {
                if (_cardConversionCoordinator == null)
                {
                    AbortInteraction(
                        nodeContentId,
                        "缺少卡牌归元选择协调器。");
                    yield break;
                }
                bool choiceResolved = false;
                bool cancelled = false;
                RunCardConversionSelection selection = default;
                Vector2 mapTargetScreenPosition = new Vector2(
                    Screen.width * 0.5f,
                    Screen.height * 0.5f);
                if (_presentation.TryCaptureInteractionScreenAnchor(
                        nodeContentId,
                        out Vector2 capturedMapAnchor))
                {
                    mapTargetScreenPosition = capturedMapAnchor;
                }
                if (!_cardConversionCoordinator.TryBegin(
                        conversionEffect,
                        mapTargetScreenPosition,
                        selected =>
                        {
                            selection = selected;
                            choiceResolved = true;
                        },
                        () =>
                        {
                            cancelled = true;
                            choiceResolved = true;
                        },
                        out string beginError))
                {
                    AbortInteraction(nodeContentId, beginError);
                    yield break;
                }
                manaConversion = _cardConversionCoordinator.IsManaConversion;
                while (true)
                {
                    while (!choiceResolved)
                        yield return null;
                    if (cancelled)
                    {
                        CancelInteraction(nodeContentId);
                        yield break;
                    }
                    executionContext = selection.IsCashOut
                        ? new LevelInteractionExecutionContext(
                            conversionEffect.effectId,
                            0,
                            useUntargetedHandler: true)
                        : new LevelInteractionExecutionContext(
                            conversionEffect.effectId,
                            selection.CardRuntimeInstanceId);
                    if (_interactionEffectExecutor.TryExecute(
                            nodeContentId,
                            executionContext,
                            out receipts,
                            out string conversionError))
                    {
                        effectsExecuted = true;
                        break;
                    }

                    choiceResolved = false;
                    if (!_cardConversionCoordinator.ShowCommitFailure(
                            conversionEffect,
                            conversionError))
                    {
                        _cardConversionCoordinator.CloseSilently();
                        AbortInteraction(nodeContentId, conversionError);
                        yield break;
                    }
                }
            }

            if (!effectsExecuted && !_interactionEffectExecutor.TryExecute(
                    nodeContentId,
                    executionContext,
                    out receipts,
                    out string executeError))
            {
                _sealAssignmentCoordinator?.CloseSilently();
                _cardConversionCoordinator?.CloseSilently();
                AbortInteraction(nodeContentId, executeError);
                yield break;
            }

            if (!floorExit && !chickenInteraction && !cardStatInteraction && !cardFacilityInteraction && !manaConversion)
                InteractionEffectsCommitted?.Invoke(nodeContentId);

            // 权威提交成功后，先在仍保持打开的归元面板中完成法意收束；
            // 玩家收好结果后，地图碑体才接续完成表现。
            if (conversionEffect != null)
            {
                bool foundReceipt = false;
                LevelInteractionReceipt conversionReceipt = default;
                for (int i = 0; receipts != null && i < receipts.Count; i++)
                {
                    if (receipts[i].Kind ==
                            LevelInteractionEffectKind.ConvertCard &&
                        string.Equals(
                            receipts[i].EffectId,
                            conversionEffect.effectId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        foundReceipt = true;
                        conversionReceipt = receipts[i];
                        break;
                    }
                }

                bool overlayClosed = false;
                bool overlayStarted = foundReceipt &&
                    _cardConversionCoordinator.PlayCommitted(
                        conversionReceipt,
                        () => overlayClosed = true);
                if (overlayStarted)
                {
                    while (!overlayClosed &&
                           _cardConversionCoordinator.IsActive)
                        yield return null;
                    if (!overlayClosed)
                    {
                        Debug.LogWarning(
                            "[LevelMapEntryCoordinator] 归元面板异常消失，" +
                            $"跳过完成等待：{nodeContentId}。");
                        _cardConversionCoordinator.CloseSilently();
                    }
                }
                else
                {
                    Debug.LogWarning(
                        "[LevelMapEntryCoordinator] 归元已提交，但专属面板" +
                        $"无法播放完成表现：{nodeContentId}。");
                    _cardConversionCoordinator.CloseSilently();
                }
            }

            // ConvertCard owns a short map-space completion. The gameplay
            // transaction has already committed, but the node must not be
            // synchronized to Completed until its state visual finishes;
            // otherwise SetState(Completed) would cancel the gathering flow.
            if (conversionEffect != null && _presentation != null)
            {
                bool presentationCompleted = false;
                bool presentationStarted =
                    _presentation.TryPlayStateVisualCompletion(
                        nodeContentId,
                        () => presentationCompleted = true);
                if (presentationStarted)
                {
                    const float timeout = 5f;
                    float elapsed = 0f;
                    while (!presentationCompleted && elapsed < timeout)
                    {
                        elapsed += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (!presentationCompleted)
                    {
                        Debug.LogWarning(
                            "[LevelMapEntryCoordinator] 归元节点完成表现超时，" +
                            $"继续结算：{nodeContentId}。");
                    }
                }
            }

            // 普通奖励在点击当帧完成结算；归元先等待上面的专属收束表现，
            // 再同步 Completed 并打开获得窗。
            if (!floorExit && !arcanaInteraction && !chickenInteraction && !cardStatInteraction && !cardFacilityInteraction && !gemInteraction &&
                !(manaConversion && _session.GetNodeState(nodeContentId) == LevelNodeViewState.Completed) && !_session.TryCompleteInteraction(
                    nodeContentId, out string error))
            {
                _sealAssignmentCoordinator?.CloseSilently();
                _cardConversionCoordinator?.CloseSilently();
                AbortInteraction(
                    nodeContentId,
                    $"完成奖励收集失败：{error}");
                yield break;
            }
            SyncNodeStatesWithSession();

            if (cardFacilityInteraction)
            {
                bool feedbackCompleted = false;
                if (cardFacility.IsActive) cardFacility.PlayCommitted(() => feedbackCompleted = true);
                while (!feedbackCompleted && cardFacility.IsActive) yield return null;
            }
            if (cardStatInteraction)
            {
                bool feedbackCompleted = false;
                if (_cardStatOfferingCoordinator != null && _cardStatOfferingCoordinator.IsActive)
                    _cardStatOfferingCoordinator.PlayCommitted(() => feedbackCompleted = true);
                while (!feedbackCompleted && _cardStatOfferingCoordinator != null && _cardStatOfferingCoordinator.IsActive)
                    yield return null;
            }
            if (chickenInteraction)
            {
                bool feedbackCompleted = false;
                if (_chickenOfferingCoordinator != null && _chickenOfferingCoordinator.IsActive)
                    _chickenOfferingCoordinator.PlayCommitted(() => feedbackCompleted = true);
                while (!feedbackCompleted && _chickenOfferingCoordinator != null && _chickenOfferingCoordinator.IsActive)
                    yield return null;
            }
            if (arcanaInteraction)
            {
                bool feedbackCompleted = false;
                if (_arcanaCoordinator != null && _arcanaCoordinator.IsActive)
                    _arcanaCoordinator.PlayCommitted(() => feedbackCompleted = true);
                while (!feedbackCompleted && _arcanaCoordinator != null && _arcanaCoordinator.IsActive)
                    yield return null;
            }

            if (runInteraction)
            {
                bool feedbackCompleted = false;
                _dungeonInteractionCoordinator.PlayCommitted(
                    () => feedbackCompleted = true);
                while (!feedbackCompleted)
                    yield return null;
            }

            if (sealEffect != null)
            {
                if (sealSelection.IsCashOut)
                {
                    _sealAssignmentCoordinator.CloseSilently();
                }
                else
                {
                    bool committedAnimationFinished = false;
                    _sealAssignmentCoordinator.PlayCommitted(
                        () => committedAnimationFinished = true);
                    while (!committedAnimationFinished)
                        yield return null;
                }
            }

            bool companionPresentationCompleted = true;
            bool waitForCompanionBeforeUnlock = false;

            // 结算已经落地，展示层只负责反馈；每个反馈都由下一次任意点击关闭。
            // 节点完成角色演出与首个奖励窗同帧启动。被配置为 PassiveOverlay 的
            // 卷灵不会抢走奖励窗点击；奖励窗关闭后也不再阻塞后续地图节点。
            bool firstVisibleFeedback = true;
            for (int i = 0; i < receipts.Count; i++)
            {
                LevelInteractionReceipt receipt = receipts[i];
                bool noOpHealing =
                    receipt.Kind == LevelInteractionEffectKind.HealCurrentHealth &&
                    !receipt.NewlyApplied &&
                    receipt.ValueAfter <= receipt.ValueBefore;
                if (runInteraction || arcanaInteraction || chickenInteraction || cardStatInteraction || cardFacilityInteraction ||
                    (!receipt.NewlyApplied && !noOpHealing) ||
                    receipt.Kind == LevelInteractionEffectKind.GrantSeal ||
                    (receipt.Kind == LevelInteractionEffectKind.ConvertCard && !manaConversion))
                    continue;
                bool dismissed = false;
                // The fixed pickup capacity is display context, never the
                // authoritative healed amount or a second settlement.
                int? configuredHealingAmount = null;
                if (receipt.Kind == LevelInteractionEffectKind.HealCurrentHealth)
                {
                    LevelInteractionEffect healing = node?.completionEffects?.Find(effect =>
                        effect != null && effect.effectId == receipt.EffectId &&
                        effect.kind == receipt.Kind && effect.amountMax == 0);
                    configuredHealingAmount = healing?.amount;
                }
                ShowInteractionReceipt(receipt, () => dismissed = true, startingManaBefore,
                    manaConversion && receipt.Kind == LevelInteractionEffectKind.ConvertCard,
                    configuredHealingAmount);
                if (firstVisibleFeedback && !string.IsNullOrWhiteSpace(
                        node?.completionPresentationId))
                {
                    companionPresentationCompleted = false;
                    Action onPresentationCompleted =
                        () => companionPresentationCompleted = true;
                    bool presentationStarted = false;
                    if (_characterPresenter != null)
                    {
                        presentationStarted = string.IsNullOrWhiteSpace(
                                node.completionPresentationVariantId)
                            ? _characterPresenter.TryPlay(
                                node.completionPresentationId,
                                onPresentationCompleted)
                            : _characterPresenter.TryPlayVariant(
                                node.completionPresentationId,
                                node.completionPresentationVariantId,
                                onPresentationCompleted);
                    }
                    if (!presentationStarted)
                    {
                        companionPresentationCompleted = true;
                    }
                    else
                    {
                        waitForCompanionBeforeUnlock =
                            _characterPresenter
                                .ActiveBlocksUnderlyingInteraction;
                    }
                }
                firstVisibleFeedback = false;
                while (!dismissed)
                {
                    if (floorExit && (_rewardRevealOverlay == null || !_rewardRevealOverlay.IsShown ||
                            !_rewardRevealOverlay.Root.activeInHierarchy))
                    {
                        Debug.LogWarning("[LevelMapEntryCoordinator] 离层已保存，获得窗已消失，继续进入已提交的新层。");
                        CompleteCommittedExitFeedback(nodeContentId);
                        yield break;
                    }
                    yield return null;
                }
            }

            if (waitForCompanionBeforeUnlock)
            {
                while (!companionPresentationCompleted)
                    yield return null;
            }

            _entering = false;
            _pendingEntry = null;
            if (_presentation != null && !floorExit)
                _presentation.SetInteractionEnabled(true);

            Debug.Log(
                $"[LevelMapEntryCoordinator] 奖励收集完成：{nodeContentId}。");

            if (node?.nodeType == LevelNodeType.Exit)
                CompleteCommittedExitFeedback(nodeContentId);
            else if (manaConversion)
                DurableInteractionCompleted?.Invoke();
            else
                InteractionCompleted?.Invoke(receipts);
        }

        private bool HasCommittedManaConversion(string nodeId)
        {
            if (_session?.GetNodeState(nodeId) != LevelNodeViewState.Completed) return false;
            var node = _session.FindNode(nodeId);
            if (node?.completionEffects == null) return false;
            foreach (var effect in node.completionEffects)
                if (MapRewardNodeStyleResolver.IsManaConversion(effect) &&
                    _session.TryGetInteractionReceipt(effect.effectId, out var receipt) &&
                    receipt.TargetRuntimeInstanceId > 0) return true;
            return false;
        }

        private void CompleteCommittedExitFeedback(string nodeContentId)
        {
            if (_committedExitFeedbackNodeId != nodeContentId) return;
            _committedExitFeedbackNodeId = null;
            _entering = false;
            _pendingEntry = null;
            // The old map remains non-interactive. This only changes scenes;
            // no new rule commit, balance mutation or persistence happens here.
            ExitCompleted?.Invoke(nodeContentId);
        }

        private LevelInteractionEffect FindPendingSealEffect(LevelNode node)
        {
            if (node?.completionEffects == null)
                return null;
            for (int i = 0; i < node.completionEffects.Count; i++)
            {
                LevelInteractionEffect effect = node.completionEffects[i];
                if (effect == null ||
                    effect.kind != LevelInteractionEffectKind.GrantSeal)
                    continue;
                if (_session.TryGetInteractionReceipt(effect.effectId, out _))
                    return null;
                return effect;
            }
            return null;
        }

        private LevelInteractionEffect FindPendingConversionEffect(
            LevelNode node)
        {
            if (node?.completionEffects == null)
                return null;
            for (int i = 0; i < node.completionEffects.Count; i++)
            {
                LevelInteractionEffect effect = node.completionEffects[i];
                if (effect == null ||
                    effect.kind != LevelInteractionEffectKind.ConvertCard)
                    continue;
                if (_session.TryGetInteractionReceipt(effect.effectId, out _))
                    return null;
                return effect;
            }
            return null;
        }

        private void AbortInteraction(string nodeContentId, string error)
        {
            Debug.LogError(
                $"[LevelMapEntryCoordinator] 交互效果结算失败 " +
                $"{nodeContentId}：{error}");
            CancelInteraction(nodeContentId);
        }

        private void CancelInteraction(string nodeContentId)
        {
            // A lost view is not a durable TryLeave. Keep the staged node and
            // frozen journal together so a click/reload can resume the choice.
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run != null && ReferenceEquals(run.CurrentFloor, _session) &&
                run.GetNodeAdmission(nodeContentId) == DungeonRunNodeAdmission.ResumePendingChoice)
            {
                _cardStatOfferingCoordinator?.CloseSilently();
                CloseCardFacilities();
                _entering = false; _pendingEntry = null;
                SyncNodeStatesWithSession();
                if (_presentation != null) _presentation.SetInteractionEnabled(true);
                Debug.LogWarning("[LevelMapEntryCoordinator] 待选内容已保留，可重新打开继续：" + nodeContentId);
                return;
            }
            _cardStatOfferingCoordinator?.CloseSilently();
            CloseCardFacilities();
            _dungeonInteractionCoordinator?.CloseSilently();
            _arcanaCoordinator?.CloseSilently();
            _chickenOfferingCoordinator?.CloseSilently();
            _session.RecoverInterruptedEncounter();
            SyncNodeStatesWithSession();
            _entering = false;
            _pendingEntry = null;
            _presentation.SetInteractionEnabled(true);
            InteractionCancelled?.Invoke(nodeContentId);
        }

        // Presentation recovery reads the saved receipt. It never repeats a choice
        // or node completion, even when a child view disappears during its animation.
        private bool TryGetCommittedArcanaReceipt(string nodeId, out LevelInteractionReceipt receipt)
        {
            receipt = default;
            if (_session == null || _session.GetNodeState(nodeId) != LevelNodeViewState.Completed)
                return false;
            LevelNode node = _session.FindNode(nodeId);
            if (node?.completionEffects == null) return false;
            foreach (var effect in node.completionEffects)
                if (effect?.kind == LevelInteractionEffectKind.ChooseArcana &&
                    _session.TryGetInteractionReceipt(effect.effectId, out receipt)) return true;
            return false;
        }

        private void CompleteCommittedArcanaFeedback(string nodeId)
        {
            _arcanaCoordinator?.CloseSilently();
            _chickenOfferingCoordinator?.CloseSilently();
            _entering = false;
            _pendingEntry = null;
            if (_presentation != null) _presentation.SetInteractionEnabled(true);
            if (TryGetCommittedArcanaReceipt(nodeId, out var receipt))
                InteractionCompleted?.Invoke(new[] { receipt });
        }

        private bool TryGetCommittedChickenReceipt(string nodeId, out LevelInteractionReceipt receipt)
        {
            receipt = default;
            if (_session == null || _session.GetNodeState(nodeId) != LevelNodeViewState.Completed) return false;
            LevelNode node = _session.FindNode(nodeId);
            if (node?.completionEffects == null) return false;
            foreach (var effect in node.completionEffects)
                if ((effect?.kind == LevelInteractionEffectKind.ChickenOffering ||
                     effect?.kind == LevelInteractionEffectKind.ManaOffering) &&
                    _session.TryGetInteractionReceipt(effect.effectId, out receipt)) return true;
            return false;
        }

        private bool TryGetCommittedCardStatReceipt(string nodeId, out LevelInteractionReceipt receipt)
        {
            receipt = default;
            if (_session == null || _session.GetNodeState(nodeId) != LevelNodeViewState.Completed) return false;
            LevelNode node = _session.FindNode(nodeId);
            if (node?.completionEffects == null) return false;
            foreach (var effect in node.completionEffects)
                if (effect?.kind == LevelInteractionEffectKind.CardStatOffering &&
                    _session.TryGetInteractionReceipt(effect.effectId, out receipt)) return true;
            return false;
        }

        private bool TryGetCommittedCardFacilityReceipt(string nodeId, out LevelInteractionReceipt receipt)
        {
            receipt = default;
            if (_session == null || _session.GetNodeState(nodeId) != LevelNodeViewState.Completed) return false;
            var node = _session.FindNode(nodeId);
            return FindCardFacility(node) != null &&
                _session.TryGetInteractionReceipt(node.completionEffects[0].effectId, out receipt);
        }

        private void CompleteCommittedCardFacilityFeedback(string nodeId)
        {
            CloseCardFacilities(); _entering = false; _pendingEntry = null;
            SyncNodeStatesWithSession();
            if (_presentation != null) _presentation.SetInteractionEnabled(true);
            if (TryGetCommittedCardFacilityReceipt(nodeId, out var receipt)) InteractionCompleted?.Invoke(new[] { receipt });
        }

        private void CompleteCommittedCardStatFeedback(string nodeId)
        {
            _cardStatOfferingCoordinator?.CloseSilently();
            _entering = false; _pendingEntry = null;
            if (_presentation != null) _presentation.SetInteractionEnabled(true);
            if (TryGetCommittedCardStatReceipt(nodeId, out var receipt)) InteractionCompleted?.Invoke(new[] { receipt });
        }

        private void CompleteCommittedChickenFeedback(string nodeId)
        {
            _chickenOfferingCoordinator?.CloseSilently();
            _entering = false; _pendingEntry = null;
            if (_presentation != null) _presentation.SetInteractionEnabled(true);
            if (TryGetCommittedChickenReceipt(nodeId, out var receipt)) InteractionCompleted?.Invoke(new[] { receipt });
        }

        private void ShowInteractionReceipt(
            LevelInteractionReceipt receipt,
            Action onClosed,
            int? startingManaBefore = null,
            bool manaConversion = false,
            int? configuredHealingAmount = null)
        {
            Canvas canvas = EnsureRewardCanvas();
            if (_rewardRevealOverlay == null)
            {
                _rewardRevealOverlay =
                    RewardFeedbackPresenter.Create(this);
            }

            RewardFeedbackItem item = manaConversion
                ? RewardFeedbackItem.FromBaseManaConversion(receipt)
                : RewardFeedbackItem.FromInteractionReceipt(receipt, configuredHealingAmount);
            if (item.Kind == RewardFeedbackKind.BaseMana)
            {
                DungeonRunSession run = DungeonRunSessionStore.Current;
                if (startingManaBefore.HasValue && run != null &&
                    run.TryGetMapStartingMana(out int startingManaAfter))
                    item.ProgressText = $"每回合基础灵力：{startingManaBefore.Value} → {startingManaAfter}";
                _rewardRevealOverlay.ShowTimed(canvas, item, 2f, onClosed);
            }
            else
                _rewardRevealOverlay.Show(canvas, item, onClosed);
        }

        private Canvas EnsureRewardCanvas()
        {
            if (_rewardCanvas != null)
                return _rewardCanvas;

            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystemObject = new GameObject(
                    "MapRewardEventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystemObject.transform.SetParent(transform, false);
            }

            var canvasObject = new GameObject(
                "MapRewardFeedbackCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _rewardCanvas = canvasObject.GetComponent<Canvas>();
            _rewardCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _rewardCanvas.overrideSorting = true;
            _rewardCanvas.sortingOrder = 32000;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return _rewardCanvas;
        }

        private void SyncNodeStatesWithSession()
        {
            if (_session == null || _session.Profile == null ||
                _presentation == null)
                return;
            DungeonRunSession run = DungeonRunSessionStore.Current;
            bool ownsRunFloor = run != null && ReferenceEquals(run.CurrentFloor, _session);

            foreach (var node in _session.Nodes)
            {
                if (node == null) continue;
                var viewState = _session.GetNodeState(node.contentId);
                var mapState = MapStateFromView(viewState);
                // Domain keeps the staged checkpoint Completing; the map must
                // still expose its legal resume action after a view interruption.
                if (ownsRunFloor && run.GetNodeAdmission(node.contentId) == DungeonRunNodeAdmission.ResumePendingChoice)
                    mapState = MapVfxState.Available;
                bool manaPickupAvailable = ownsRunFloor &&
                    run.CardConversions.HasPendingManaPickup(run.CurrentFloorContentId, node.contentId);
                if (manaPickupAvailable) mapState = MapVfxState.Completed;
                bool pickupAvailable = ownsRunFloor &&
                    (run.AbandonedCarts.HasPendingPickup(run.CurrentFloorContentId, node.contentId) ||
                     manaPickupAvailable);
                _presentation.SetGroupState(node.contentId, mapState, immediate: true,
                    pickupAvailable: pickupAvailable);
            }
        }

        private static MapVfxState MapStateFromView(LevelNodeViewState viewState)
        {
            return viewState switch
            {
                LevelNodeViewState.Hidden => MapVfxState.Hidden,
                LevelNodeViewState.Locked => MapVfxState.Locked,
                LevelNodeViewState.Available => MapVfxState.Available,
                LevelNodeViewState.Completing => MapVfxState.Completing,
                LevelNodeViewState.Completed => MapVfxState.Completed,
                _ => MapVfxState.Hidden
            };
        }

        /// <summary>恢复中断的遭遇（转场被拒绝时调用）。</summary>
        public void RecoverInterruptedEncounter()
        {
            FloorCombatSceneOwner.Current?.DiscardPreparedEncounter(_session);
            ReleaseCombatLoading();
            if (_pendingEntry != null)
            {
                StopCoroutine(_pendingEntry);
                _pendingEntry = null;
            }
            var run = DungeonRunSessionStore.Current;
            string interaction = _session?.ActiveInteractionNodeId;
            if (run != null && ReferenceEquals(run.CurrentFloor, _session) &&
                !string.IsNullOrEmpty(interaction) &&
                run.GetNodeAdmission(interaction) == DungeonRunNodeAdmission.ResumePendingChoice)
            {
                // Recover a lost view, never clear the Domain's saved choice.
                CancelInteraction(interaction);
                return;
            }
            CloseCardFacilities();
            if (_committedExitFeedbackNodeId != null)
            {
                _rewardRevealOverlay?.CloseSilently();
                CompleteCommittedExitFeedback(_committedExitFeedbackNodeId);
                return;
            }
            // The map still owns all of its injected references at this point.
            // Keep the aggregate instance stable while undoing an entry that
            // never reached Combat; replacing the hosted Run here would leave
            // map coordinators pointing at a detached floor.  The checkpoint
            // is still the save boundary for any hidden preload writes, while
            // the in-memory entry mutation is cleared by the floor owner.
            _crystalCoordinator?.CloseSilently();
            if (_session != null)
                _session.RecoverInterruptedEncounter();
            if (DungeonRunSessionStore.HasCombatRollbackCheckpoint)
                DungeonRunSessionStore.DiscardCombatRollbackCheckpoint();
            _rewardRevealOverlay?.CloseSilently();
            _sealAssignmentCoordinator?.CloseSilently();
            _cardConversionCoordinator?.CloseSilently();
            _dungeonInteractionCoordinator?.CloseSilently();
            _arcanaCoordinator?.CloseSilently();
            _chickenOfferingCoordinator?.CloseSilently();
            _entering = false;
            if (_presentation != null)
                _presentation.SetInteractionEnabled(true);
        }

        /// <summary>重置进入状态（战斗返回后调用）。</summary>
        public void ResetEntering()
        {
            ReleaseCombatLoading();
            _crystalCoordinator?.CloseSilently();
            _manaStatueCoordinator?.CloseSilently();
            _manaPlatformCoordinator?.CloseSilently();
            _entering = false;
            if (_presentation != null)
                _presentation.SetInteractionEnabled(true);
            _manaPlatformCoordinator?.ResumeLegacyPending();
        }

        private void BeginTransition(string requestId, SceneContextBuilder context)
        {
            float timeout = _transitionProfile != null
                ? _transitionProfile.destinationReadyTimeout
                : 15f;

            var request = new SceneTransitionRequest(
                requestId,
                SceneNames.Combat,
                context,
                timeout);

            StopWatchingCombatTransition();
            _transitionRuntime = SceneTransitionRuntimeBootstrap.Ensure();
            _combatTransitionRequestId = requestId;
            _combatTransitionRun = DungeonRunSessionStore.Current;
            _transitionRuntime.TransitionCompleted += OnCombatTransitionCompleted;

            if (!SceneTransitionRuntime.TryBegin(request, out string error))
            {
                StopWatchingCombatTransition();
                Debug.LogWarning(
                    $"[LevelMapEntryCoordinator] 转场被拒绝：{error}");
                RecoverInterruptedEncounter();
                return;
            }

            ReleaseCombatLoading();

            // Transition started — interaction barrier holds until reveal
            Debug.Log(
                $"[LevelMapEntryCoordinator] 已发起战斗转场，requestId={requestId}。");
        }

        private void OnCombatTransitionCompleted(SceneTransitionResult result)
        {
            if (!string.Equals(result.RequestId, _combatTransitionRequestId,
                    StringComparison.Ordinal)) return;
            DungeonRunSession run = _combatTransitionRun;
            StopWatchingCombatTransition();
            if (result.IsSuccess || !OwnsActiveSourceMap(run)) return;

            // A hidden build may still be writing its pre-combat checkpoint.
            // Let its single owner finish cleanup before removing that checkpoint.
            _combatPreloader?.InvalidatePreparedRunState();
            FloorCombatSceneOwner.Current?.DiscardPreparedEncounter(_session);
            _pendingEntry = StartCoroutine(RecoverAfterFailedTransition(run));
        }

        private IEnumerator RecoverAfterFailedTransition(DungeonRunSession run)
        {
            yield return null;
            while ((_combatPreloader != null && _combatPreloader.IsBusy) ||
                (FloorCombatSceneOwner.Current != null &&
                 ReferenceEquals(FloorCombatSceneOwner.Current.Floor, _session) &&
                 FloorCombatSceneOwner.Current.IsBusy))
                yield return null;
            _pendingEntry = null;
            if (OwnsActiveSourceMap(run)) RecoverInterruptedEncounter();
        }

        private bool OwnsActiveSourceMap(DungeonRunSession run) =>
            gameObject.activeInHierarchy && _presentation != null &&
            gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene() &&
            ReferenceEquals(run, DungeonRunSessionStore.Current) &&
            (run == null || ReferenceEquals(run.CurrentFloor, _session));

        internal void RestoreInputAfterRejectedMapReload()
        {
            var run = DungeonRunSessionStore.Current;
            if (run != null && OwnsActiveSourceMap(run))
                _presentation.SetInteractionEnabled(true);
        }

        private void StopWatchingCombatTransition()
        {
            if (_transitionRuntime != null)
                _transitionRuntime.TransitionCompleted -= OnCombatTransitionCompleted;
            _transitionRuntime = null;
            _combatTransitionRequestId = null;
            _combatTransitionRun = null;
        }

        private void ReleaseCombatLoading()
        {
            var runtime = _combatPreparationRuntime;
            string requestId = _combatPreparationRequestId;
            _combatPreparationRuntime = null;
            _combatPreparationRequestId = null;
            // After TryBegin, the same Core request owns the transition lease;
            // this cancellation is deliberately limited to pre-load preparation.
            if (runtime != null && requestId != null)
                runtime.CancelBattlePreparation(requestId);
        }

        private void OnDestroy()
        {
            StopWatchingCombatTransition();
            _manaPlatformCoordinator?.CloseSilently();
            ReleaseCombatLoading();
            CloseCardFacilities();
            _cardStatOfferingCoordinator?.CloseSilently();
            _crystalCoordinator?.CloseSilently();
            _rewardRevealOverlay?.CloseSilently();
            _sealAssignmentCoordinator?.CloseSilently();
            _cardConversionCoordinator?.CloseSilently();
            _dungeonInteractionCoordinator?.CloseSilently();
            _arcanaCoordinator?.CloseSilently();
            _chickenOfferingCoordinator?.CloseSilently();
            _pendingEntry = null;
            _manaStatueCoordinator?.CloseSilently();
            _entering = false;
        }
    }
}
