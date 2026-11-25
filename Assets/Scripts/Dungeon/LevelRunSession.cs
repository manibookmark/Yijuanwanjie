using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>遭遇启动信息。传给战斗场景。</summary>
    public readonly struct LevelEncounterLaunch
    {
        public string NodeContentId { get; }
        public string ScenarioId { get; }
        public string RunId { get; }

        public LevelEncounterLaunch(
            string nodeContentId,
            string scenarioId,
            string runId)
        {
            NodeContentId = nodeContentId;
            ScenarioId = scenarioId;
            RunId = runId;
        }
    }

    /// <summary>遭遇返回结果。战斗结束后传回关卡。</summary>
    public readonly struct LevelEncounterReturn
    {
        public string NodeContentId { get; }
        public CombatOutcome Outcome { get; }

        public LevelEncounterReturn(
            string nodeContentId,
            CombatOutcome outcome)
        {
            NodeContentId = nodeContentId;
            Outcome = outcome;
        }
    }

    /// <summary>
    /// 关卡运行会话。维护跨场景的节点状态和遭遇生命周期。
    /// </summary>
    public sealed class LevelRunSession :
        ILevelVictorySealRewardCommitTarget
    {
        private readonly Dictionary<string, LevelNodeProgress> _nodeStates
            = new Dictionary<string, LevelNodeProgress>();
        private readonly Dictionary<string, LevelInteractionReceipt>
            _appliedInteractionEffects =
                new Dictionary<string, LevelInteractionReceipt>();
        private LevelVictorySealRewardStateMachine _victorySealRewards =
            new LevelVictorySealRewardStateMachine();
        private LevelEncounterLaunch? _activeEncounter;
        private string _activeInteractionNodeId;
        private LevelEncounterReturn? _pendingReturn;
        private bool _returnConsumed;
        private int _encounterCount;
        private int _victoryCount;
        private readonly string _snapshotId;
        private readonly DungeonRunSharedState _shared;
        // Frozen Run-owned bindings add prerequisites without rewriting the
        // shared author profile. This view owns no random stream or claim state.
        private readonly LevelExplorationGuardView _explorationGuards;
        internal LevelExplorationGuardView ExplorationGuards => _explorationGuards;
        private LevelRunNodeDirectory _nodes;
        internal LevelRunNodeDirectory NodeDirectory => _nodes;

        /// <summary>Detached effective contracts; querying never draws or mutates an author asset.</summary>
        public IReadOnlyList<LevelNode> Nodes => _nodes.Nodes;
        /// <summary>Runtime lookup excludes unused slots; the directory retains their full saved topology.</summary>
        public LevelNode FindNode(string nodeContentId)
            => IsNodeEnabled(nodeContentId) ? _nodes.FindNode(nodeContentId) : null;
        public bool IsNodeEnabled(string nodeContentId) => _nodes.IsNodeEnabled(nodeContentId);

        /// <summary>Checks the complete effective payload without relying on detached object identity.</summary>
        public bool MatchesConfiguredEffect(string nodeContentId, LevelInteractionEffect supplied)
            => _nodes.MatchesEffect(nodeContentId, supplied);

        public string RunId { get; }
        public LevelRunProfile Profile { get; }
        public string SnapshotId => _snapshotId;
        public LevelEncounterLaunch? ActiveEncounter => _activeEncounter;
        public string ActiveInteractionNodeId => _activeInteractionNodeId;
        public CombatProgressionState CombatProgression =>
            _shared.CombatProgression;
        public RunPresentationHistory Presentations { get; }
        public LevelRunModifierSnapshot Modifiers =>
            new LevelRunModifierSnapshot(
                _shared.BaseManaBonus,
                _shared.BaseArmorPerTurnBonus,
                _shared.MightBonus,
                _shared.HandLimitBonus,
                _shared.AreaBonus);
        public int EncounterCount => _encounterCount;
        public int VictoryCount => _victoryCount;
        public int RewardRerollsRemaining =>
            _shared.RewardRerollsRemaining;

        /// <summary>Reads this floor's frozen binding; never draws or exposes mutable Run state.</summary>
        public bool TryGetExplorationGuard(string rewardNodeId, out string guardId)
            => _explorationGuards.TryGetGuard(rewardNodeId, out guardId);

        /// <summary>
        /// Exit may resume its own interaction, but may not abandon combat, a map
        /// return or another mandatory settlement. Optional unvisited nodes are not gates.
        /// </summary>
        internal void RequireFloorExitReady(string exitNodeId = null)
        {
            if (_activeEncounter.HasValue || _pendingReturn.HasValue ||
                _victorySealRewards.TryGetPending(out _) ||
                (!string.IsNullOrWhiteSpace(_activeInteractionNodeId) &&
                 !string.Equals(_activeInteractionNodeId, exitNodeId, StringComparison.Ordinal)))
                throw new InvalidOperationException("请先完成战斗、返回地图及当前强制奖励结算，再离层。");
            foreach (var node in _nodeStates)
                if (node.Value == LevelNodeProgress.Resolving && node.Key != exitNodeId)
                    throw new InvalidOperationException("仍有未完成的节点结算，不能离层：" + node.Key);
        }

        internal LevelRunSession(
            LevelRunProfile profile,
            string snapshotId,
            string runId,
            DungeonRunSharedState shared = null,
            bool rebuildInteractionEffects = true,
            LevelExplorationGuardView explorationGuards = null,
            LevelRunNodeDirectory nodeDirectory = null)
        {
            Profile = profile
                ?? throw new ArgumentNullException(nameof(profile));
            _nodes = nodeDirectory ?? LevelRunNodeDirectory.ForAuthor(profile);
            _nodes.RequireMatches(profile);
            _explorationGuards = explorationGuards ?? LevelExplorationGuardView.Empty;
            _explorationGuards.RequireMatches(profile);
            foreach (LevelNode node in Nodes)
                if (IsNodeEnabled(node.contentId) &&
                    _explorationGuards.TryGetGuard(node.contentId, out string guardId) && !IsNodeEnabled(guardId))
                    throw new InvalidOperationException("An enabled reward cannot depend on a disabled exploration guard.");
            RunId = runId ?? Guid.NewGuid().ToString("N");
            _snapshotId = snapshotId;
            _shared = shared ?? new DungeonRunSharedState(
                profile.levelExperienceRules,
                new RunProgressionState());
            Presentations = new RunPresentationHistory();

            var snapshot = profile.FindSnapshot(snapshotId)
                ?? throw new ArgumentException(
                    $"快照不存在：{snapshotId}",
                    nameof(snapshotId));

            for (int i = 0; i < snapshot.nodeStates.Count; i++)
            {
                var state = snapshot.nodeStates[i];
                LevelNodeProgress initial = state.progress;
                if (_nodes.HasFrozenInteractions && !IsNodeEnabled(state.nodeContentId))
                {
                    if (initial != LevelNodeProgress.Locked && initial != LevelNodeProgress.Available)
                        throw new InvalidOperationException("A disabled side slot cannot contain resolved or completed snapshot progress.");
                    initial = LevelNodeProgress.Locked;
                }
                _nodeStates[state.nodeContentId] = initial;
            }

            // 初态统一按前置推导：无前置 = 可执行，前置未完成 = 锁定。
            // 快照中的 Locked/Available 只是草稿值，不再作为权威初态。
            ReconcileNodeAvailability();

            if (rebuildInteractionEffects)
                RebuildInteractionEffects(snapshot);
        }

        public LevelNodeViewState GetNodeState(string nodeContentId)
        {
            if ((_nodes.HasFrozenInteractions && !IsNodeEnabled(nodeContentId)) ||
                !_nodeStates.TryGetValue(nodeContentId, out var progress))
                return LevelNodeViewState.Hidden;

            return progress switch
            {
                LevelNodeProgress.Locked => LevelNodeViewState.Locked,
                LevelNodeProgress.Available => LevelNodeViewState.Available,
                LevelNodeProgress.Resolving => LevelNodeViewState.Completing,
                LevelNodeProgress.Cleared => LevelNodeViewState.Completed,
                _ => LevelNodeViewState.Hidden
            };
        }

        /// <summary>
        /// Opens every declared node for an explicit analysis sequence.
        /// The profile's prerequisiteNodeIds remain unchanged and normal game
        /// sessions never call this method. An analysis sequence can therefore
        /// observe declared content order without turning that declaration into
        /// a second, accidental map-gating contract.
        /// </summary>
        public void PrepareForExplicitSequence()
        {
            if (_activeEncounter.HasValue ||
                !string.IsNullOrWhiteSpace(_activeInteractionNodeId) ||
                _pendingReturn.HasValue)
            {
                throw new InvalidOperationException(
                    "Cannot prepare a non-quiescent run for an explicit analysis sequence.");
            }

            IReadOnlyList<LevelNode> effectiveNodes = Nodes;
            for (int i = 0; i < effectiveNodes.Count; i++)
            {
                LevelNode node = effectiveNodes[i];
                if (node == null ||
                    !IsNodeEnabled(node.contentId) ||
                    !_nodeStates.TryGetValue(
                        node.contentId,
                        out LevelNodeProgress progress) ||
                    progress != LevelNodeProgress.Locked)
                {
                    continue;
                }

                _nodeStates[node.contentId] = LevelNodeProgress.Available;
            }
        }

        /// <summary>
        /// 正式进入地图前，把只发放全局唯一遗珍、且遗珍均已拥有的纯遗珍节点
        /// 视为完成。节点关系仍完全由当前 Profile 推导；混合奖励节点不会被跳过。
        /// </summary>
        public int ReconcileOwnedUniqueRelicNodes(
            Func<string, bool> isRelicOwned)
        {
            if (isRelicOwned == null)
                throw new ArgumentNullException(nameof(isRelicOwned));

            int reconciledCount = 0;
            IReadOnlyList<LevelNode> effectiveNodes = Nodes;
            for (int i = 0; i < effectiveNodes.Count; i++)
            {
                LevelNode node = effectiveNodes[i];
                if (node == null ||
                    node.nodeType != LevelNodeType.Interaction ||
                    !IsNodeEnabled(node.contentId) ||
                    node.completionEffects == null ||
                    node.completionEffects.Count == 0 ||
                    !_nodeStates.TryGetValue(
                        node.contentId, out LevelNodeProgress progress) ||
                    progress == LevelNodeProgress.Cleared ||
                    progress == LevelNodeProgress.Resolving)
                {
                    continue;
                }

                bool allEffectsAreOwnedUniqueRelics = true;
                for (int effectIndex = 0;
                     effectIndex < node.completionEffects.Count;
                     effectIndex++)
                {
                    LevelInteractionEffect effect =
                        node.completionEffects[effectIndex];
                    if (effect == null ||
                        effect.kind != LevelInteractionEffectKind.GrantRelic ||
                        string.IsNullOrWhiteSpace(effect.targetContentId) ||
                        !isRelicOwned(effect.targetContentId))
                    {
                        allEffectsAreOwnedUniqueRelics = false;
                        break;
                    }
                }

                if (!allEffectsAreOwnedUniqueRelics)
                    continue;

                _nodeStates[node.contentId] = LevelNodeProgress.Cleared;
                reconciledCount++;
            }

            if (reconciledCount > 0)
                ReconcileNodeAvailability();
            return reconciledCount;
        }

        public bool TryBeginEncounter(
            string nodeContentId,
            out LevelEncounterLaunch launch,
            out string error)
        {
            launch = default;
            error = null;

            if (_pendingReturn.HasValue)
            {
                error = "上一场遭遇仍有未确认的返回结果。";
                return false;
            }

            if (_activeEncounter.HasValue)
            {
                error = "已有进行中的遭遇。";
                return false;
            }

            if (!string.IsNullOrEmpty(_activeInteractionNodeId))
            {
                error = "已有进行中的地图交互。";
                return false;
            }

            if (!_nodeStates.TryGetValue(nodeContentId, out var progress))
            {
                error = $"节点不存在：{nodeContentId}";
                return false;
            }

            if (progress != LevelNodeProgress.Available)
            {
                error = $"节点不在可执行状态：{progress}。";
                return false;
            }

            var node = FindNode(nodeContentId);
            if (node == null)
            {
                error = $"节点配置不存在：{nodeContentId}";
                return false;
            }

            if (node.nodeType != LevelNodeType.Encounter &&
                node.nodeType != LevelNodeType.Finale)
            {
                error = $"节点类型 {node.nodeType} 不在当前切片支持范围。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(node.scenarioId))
            {
                error = $"遭遇节点缺少 scenarioId：{nodeContentId}";
                return false;
            }

            _nodeStates[nodeContentId] = LevelNodeProgress.Resolving;
            launch = new LevelEncounterLaunch(
                nodeContentId,
                node.scenarioId,
                RunId);
            _activeEncounter = launch;
            return true;
        }

        public bool TryCompleteEncounter(
            string runId,
            string nodeContentId,
            CombatOutcome outcome,
            out LevelEncounterReturn result,
            out string error)
            => TryCompleteEncounter(
                runId,
                nodeContentId,
                outcome,
                resolvedVictorySealContentIds: null,
                out result,
                out error);

        /// <summary>
        /// Completes an encounter after the outer Run has optionally resolved
        /// a versioned random Finale seal pool.  The resolved IDs are passed
        /// in once and immediately frozen by the floor reward state machine;
        /// the floor never queries account unlocks or a random source.
        /// </summary>
        public bool TryCompleteEncounter(
            string runId,
            string nodeContentId,
            CombatOutcome outcome,
            IReadOnlyList<string> resolvedVictorySealContentIds,
            out LevelEncounterReturn result,
            out string error, bool allowSealRewards = true)
        {
            result = default;
            error = null;

            if (runId != RunId)
            {
                error = $"runId 不匹配：期望 {RunId}，收到 {runId}。";
                return false;
            }

            if (!_activeEncounter.HasValue ||
                _activeEncounter.Value.NodeContentId != nodeContentId)
            {
                error = "没有匹配的活动遭遇。";
                return false;
            }

            if (outcome == CombatOutcome.Victory)
            {
                LevelNode node = FindNode(nodeContentId);
                LevelRewardGrant sealChoice =
                    FindVictorySealChoice(node);
                if (sealChoice != null)
                {
                    IReadOnlyList<string> sealContentIds =
                        !string.IsNullOrWhiteSpace(
                            sealChoice.sealRewardPoolId)
                            ? resolvedVictorySealContentIds
                            : GetSealChoiceContentIds(sealChoice);
                    if (allowSealRewards && !string.IsNullOrWhiteSpace(
                            sealChoice.sealRewardPoolId) &&
                        sealContentIds == null)
                    {
                        error = "随机胜利灵印奖励尚未冻结候选。";
                        return false;
                    }
                    bool cashOutOnly = !allowSealRewards || (!string.IsNullOrWhiteSpace(sealChoice.sealRewardPoolId) &&
                        sealContentIds.Count == 0 && sealChoice.allowCashOut);
                    bool started = cashOutOnly
                        ? _victorySealRewards.TryBeginCashOutOnly(nodeContentId,
                            sealChoice.contentId, sealChoice.presentationId, out _, out error)
                        : _victorySealRewards.TryBegin(
                            nodeContentId,
                            sealChoice.contentId,
                            sealContentIds,
                            sealChoice.presentationId,
                            sealChoice.allowCashOut,
                            out _,
                            out error);
                    if (!started)
                        return false;
                }
                else
                {
                    _nodeStates[nodeContentId] = LevelNodeProgress.Cleared;
                    UnlockDependentNodes();
                }
                _victoryCount++;
            }
            else
            {
                _nodeStates[nodeContentId] = LevelNodeProgress.Available;
            }
            _encounterCount++;

            result = new LevelEncounterReturn(nodeContentId, outcome);
            _pendingReturn = result;
            _returnConsumed = false;
            _activeEncounter = null;
            return true;
        }

        /// <summary>查看未确认的返回结果。可重复读取，不清除。</summary>
        public bool TryGetPendingReturn(out LevelEncounterReturn result)
        {
            if (_pendingReturn.HasValue)
            {
                result = _pendingReturn.Value;
                return true;
            }
            result = default;
            return false;
        }

        public bool TryGetPendingVictorySealReward(
            out LevelVictorySealRewardOffer offer)
            => _victorySealRewards.TryGetPending(out offer);
        internal void RestrictPendingVictorySealToCashOut() => _victorySealRewards.RestrictPendingToCashOut();

        public bool TryGetVictorySealRewardReceipt(
            string rewardContentId,
            out LevelVictorySealRewardReceipt receipt)
            => _victorySealRewards.TryGetReceipt(
                rewardContentId, out receipt);

        public bool TryBeginVictorySealAssignment(
            string rewardContentId,
            out string error)
        {
            if (!TryValidatePendingVictorySealReward(
                    rewardContentId, out _, out error))
                return false;
            return _victorySealRewards.TryBeginAssignment(
                rewardContentId, out error);
        }

        /// <summary>在固定多印胜利奖励中提交玩家选择的稳定灵印 ID。</summary>
        public bool TryChooseVictorySeal(
            string rewardContentId,
            string sealContentId,
            out string error)
        {
            error = null;
            if (!TryValidatePendingVictorySealReward(
                    rewardContentId, out _, out error))
                return false;
            return _victorySealRewards.TryChooseSeal(
                rewardContentId,
                sealContentId,
                out _,
                out error);
        }

        public bool TryReturnToVictorySealDisposition(
            string rewardContentId,
            out string error)
        {
            if (!TryValidatePendingVictorySealReward(
                    rewardContentId, out _, out error))
                return false;
            return _victorySealRewards.TryReturnToDisposition(
                rewardContentId, out error);
        }

        /// <summary>撤销尚未结算的已选灵印并回到固定候选页。</summary>
        public bool TryReturnToVictorySealChoice(
            string rewardContentId,
            out string error)
        {
            if (!TryValidatePendingVictorySealReward(
                    rewardContentId, out _, out error))
                return false;
            return _victorySealRewards.TryReturnToSealChoice(
                rewardContentId, out error);
        }

        public bool TryCommitVictorySealAssignment(
            string rewardContentId,
            int cardRuntimeInstanceId,
            int slotIndex,
            out LevelVictorySealRewardReceipt receipt,
            out string error)
        {
            receipt = default;
            if (!TryValidatePendingVictorySealReward(
                    rewardContentId,
                    out LevelVictorySealRewardOffer pending,
                    out error))
                return false;
            if (!_victorySealRewards.TryCompleteAssignment(
                    rewardContentId,
                    cardRuntimeInstanceId,
                    slotIndex,
                    out receipt,
                    out error))
                return false;
            CompleteVictorySealRewardNode(pending.NodeContentId);
            return true;
        }

        public bool TryCommitVictorySealCashOut(
            string rewardContentId,
            int baseJade,
            int finalJade,
            out LevelVictorySealRewardReceipt receipt,
            out string error)
        {
            receipt = default;
            if (!TryValidatePendingVictorySealReward(
                    rewardContentId,
                    out LevelVictorySealRewardOffer pending,
                    out error))
                return false;
            if (!_victorySealRewards.TryCompleteCashOut(
                    rewardContentId,
                    baseJade,
                    finalJade,
                    out receipt,
                    out error))
                return false;
            CompleteVictorySealRewardNode(pending.NodeContentId);
            return true;
        }

        /// <summary>
        /// 开始非战斗交互。将节点标记为 Resolving；效果全部登记后，
        /// 调用方通过 <see cref="TryCompleteInteraction"/> 完成并解锁后续节点。
        /// </summary>
        public bool TryBeginInteraction(
            string nodeContentId,
            out string error)
        {
            error = null;

            if (!IsNodeEnabled(nodeContentId))
            {
                error = $"节点不存在或未在本局启用：{nodeContentId}。";
                return false;
            }

            if (_pendingReturn.HasValue)
            {
                error = "上一场遭遇仍有未确认的返回结果。";
                return false;
            }

            if (_activeEncounter.HasValue)
            {
                error = "已有进行中的遭遇。";
                return false;
            }

            if (!string.IsNullOrEmpty(_activeInteractionNodeId))
            {
                if (_activeInteractionNodeId == nodeContentId &&
                    _nodeStates.TryGetValue(
                        nodeContentId, out var activeProgress) &&
                    activeProgress == LevelNodeProgress.Resolving)
                    return true;

                error = "已有进行中的地图交互。";
                return false;
            }

            if (!_nodeStates.TryGetValue(nodeContentId, out var progress))
            {
                error = $"节点不存在：{nodeContentId}";
                return false;
            }

            if (progress != LevelNodeProgress.Available)
            {
                error = $"节点不在可执行状态：{progress}。";
                return false;
            }

            var node = FindNode(nodeContentId);
            if (node == null)
            {
                error = $"节点配置不存在：{nodeContentId}";
                return false;
            }

            if (node.nodeType != LevelNodeType.Interaction &&
                node.nodeType != LevelNodeType.Exit)
            {
                error = $"节点类型 {node.nodeType} 不是交互节点。";
                return false;
            }

            _nodeStates[nodeContentId] = LevelNodeProgress.Resolving;
            _activeInteractionNodeId = nodeContentId;
            return true;
        }

        /// <summary>
        /// 应用只影响当前 Run 的交互效果；同一 effectId 重试时返回原回执。
        /// </summary>
        public bool TryApplyRunEffect(
            string nodeContentId,
            LevelInteractionEffect effect,
            out LevelInteractionReceipt receipt,
            out string error)
        {
            receipt = default;
            error = null;

            if (_activeInteractionNodeId != nodeContentId)
            {
                error = "没有匹配的活动地图交互。";
                return false;
            }

            if (!TryValidateConfiguredEffect(
                    nodeContentId, effect, out error))
                return false;
            if (_appliedInteractionEffects.TryGetValue(
                    effect.effectId, out LevelInteractionReceipt applied))
            {
                receipt = AsRetryReceipt(applied);
                return true;
            }

            int before;
            int after;
            switch (effect.kind)
            {
                case LevelInteractionEffectKind.AddBaseMana:
                    before = _shared.BaseManaBonus;
                    _shared.AddBaseMana(effect.amount);
                    after = _shared.BaseManaBonus;
                    // Only a successfully committed map mana pickup produces
                    // the permanent unlock fact.  Card mana effects and
                    // snapshot rebuilds use different owners and never enter
                    // this transaction boundary.
                    _shared.RunProgression.CardUnlockFacts.RecordWorldFact(
                        CardUnlockMetric.ManaCollected);
                    break;
                case LevelInteractionEffectKind.AddBaseArmor:
                    before = _shared.BaseArmorPerTurnBonus;
                    _shared.AddBaseArmorPerTurn(effect.amount);
                    after = _shared.BaseArmorPerTurnBonus;
                    break;
                case LevelInteractionEffectKind.AddHandLimit:
                    before = _shared.HandLimitBonus;
                    _shared.AddHandLimit(effect.amount);
                    after = _shared.HandLimitBonus;
                    break;
                case LevelInteractionEffectKind.AddMight:
                    before = _shared.MightBonus;
                    _shared.AddMight(effect.amount);
                    after = _shared.MightBonus;
                    break;
                case LevelInteractionEffectKind.AddArea:
                    before = _shared.AreaBonus;
                    _shared.AddArea(effect.amount);
                    after = _shared.AreaBonus;
                    break;
                case LevelInteractionEffectKind.AddRewardReroll:
                    before = _shared.RewardRerollsRemaining;
                    _shared.AddRewardRerolls(effect.amount);
                    after = _shared.RewardRerollsRemaining;
                    break;
                case LevelInteractionEffectKind.RecordStoryClue:
                    // 回执本身是唯一权威状态。数值只表示线索已获取，
                    // 不另建 flag 集合，避免存档出现两份事实。
                    before = 0;
                    after = effect.amount;
                    break;
                default:
                    error = $"交互效果 {effect.effectId} 需要外部处理器：{effect.kind}。";
                    return false;
            }

            receipt = CreateReceipt(
                effect, effect.amount, before, after,
                newlyApplied: true);
            _appliedInteractionEffects.Add(effect.effectId, receipt);
            return true;
        }

        /// <summary>登记由 Bootstrap 外部处理器成功提交的效果。</summary>
        public bool TryRecordExternalEffect(
            string nodeContentId,
            LevelInteractionEffect effect,
            int valueBefore,
            int valueAfter,
            int appliedAmount,
            out LevelInteractionReceipt receipt,
            out string error,
            int targetRuntimeInstanceId = 0,
            string resolvedTargetContentId = null)
        {
            receipt = default;
            error = null;
            if (_activeInteractionNodeId != nodeContentId)
            {
                error = "没有匹配的活动地图交互。";
                return false;
            }
            if (!TryValidateConfiguredEffect(
                    nodeContentId, effect, out error))
                return false;
            if (effect.kind == LevelInteractionEffectKind.DuplicateOffering &&
                !DungeonRunDuplicateOfferings.IsValidFloorSummary(valueBefore, valueAfter, appliedAmount, targetRuntimeInstanceId, resolvedTargetContentId))
            { error = "Duplicate completion must summarize one new permanent physical copy."; return false; }
            if (effect.kind == LevelInteractionEffectKind.CrawlerInvitation &&
                !DungeonRunInvitations.IsValidFloorSummary(valueBefore, valueAfter, appliedAmount, targetRuntimeInstanceId, resolvedTargetContentId))
            { error = "Invitation completion must identify one new summon card."; return false; }
            if (effect.kind == LevelInteractionEffectKind.GunForHire &&
                !DungeonRunGunForHire.IsValidFloorSummary(valueBefore, valueAfter, appliedAmount, targetRuntimeInstanceId, resolvedTargetContentId))
            { error = "Hire completion differs from the bought physical card or compensation."; return false; }
            if (effect.kind == LevelInteractionEffectKind.CardStatOffering &&
                !DungeonRunCardStatOfferings.IsValidFloorSummary(valueBefore, valueAfter, appliedAmount, targetRuntimeInstanceId, resolvedTargetContentId))
            { error = "CardStat completion summarizes one destroyed card or the fixed cash payout."; return false; }
            if (effect.kind == LevelInteractionEffectKind.CrystalThrowing &&
                (valueBefore != 0 || appliedAmount < 0 || valueAfter != appliedAmount ||
                 targetRuntimeInstanceId != 0 || !string.IsNullOrEmpty(resolvedTargetContentId)))
            { error = "Crystal completion summarizes actual awarded XP; it never grants XP twice."; return false; }
            if (effect.kind == LevelInteractionEffectKind.AbandonedCart &&
                (valueBefore != 3 || valueAfter != 4 || appliedAmount != 0 ||
                 targetRuntimeInstanceId != 0 || !string.IsNullOrEmpty(resolvedTargetContentId)))
            {
                error = "Cart completion records destruction only (3 → 4); the bracer is a separate Run pickup.";
                return false;
            }
            if (_appliedInteractionEffects.TryGetValue(
                    effect.effectId, out LevelInteractionReceipt applied))
            {
                receipt = AsRetryReceipt(applied);
                return true;
            }

            receipt = new LevelInteractionReceipt(
                effect.effectId, effect.kind,
                resolvedTargetContentId ?? effect.targetContentId,
                effect.presentationId, appliedAmount, valueBefore, valueAfter,
                newlyApplied: effect.kind == LevelInteractionEffectKind.HealCurrentHealth ||
                    (effect.kind == LevelInteractionEffectKind.ConvertCard && targetRuntimeInstanceId > 0) ||
                    effect.kind == LevelInteractionEffectKind.CrawlerInvitation || effect.kind == LevelInteractionEffectKind.DuplicateOffering || effect.kind == LevelInteractionEffectKind.GunForHire || effect.kind == LevelInteractionEffectKind.CardStatOffering || effect.kind == LevelInteractionEffectKind.CrystalThrowing || effect.kind == LevelInteractionEffectKind.ChickenOffering ||
                    effect.kind == LevelInteractionEffectKind.AbandonedCart ||
                    effect.kind == LevelInteractionEffectKind.ReachLevel ||
                    appliedAmount > 0,
                effect.summaryText, effect.detailText,
                effect.rarity, targetRuntimeInstanceId);
            _appliedInteractionEffects.Add(effect.effectId, receipt);
            return true;
        }

        /// <summary>读取已成功登记的效果回执，用于失败重试时避免重复外部提交。</summary>
        public bool TryGetInteractionReceipt(
            string effectId,
            out LevelInteractionReceipt receipt)
        {
            if (_appliedInteractionEffects.TryGetValue(
                    effectId, out LevelInteractionReceipt applied))
            {
                receipt = AsRetryReceipt(applied);
                return true;
            }
            receipt = default;
            return false;
        }

        /// <summary>
        /// 查询当前 Run 是否已经结算过指定类型和目标的交互效果。Bootstrap
        /// 可据此把稳定内容 ID 映射为战斗规则，无需 Dungeon 认识具体遗珍。
        /// </summary>
        public bool HasAppliedInteractionTarget(
            LevelInteractionEffectKind kind,
            string targetContentId)
        {
            if (string.IsNullOrWhiteSpace(targetContentId))
                return false;
            foreach (LevelInteractionReceipt receipt in
                     _appliedInteractionEffects.Values)
            {
                if (receipt.Kind == kind &&
                    string.Equals(
                        receipt.TargetContentId,
                        targetContentId,
                        StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Cancel only the exact unsubmitted interaction; no receipt or dependent unlock is produced.</summary>
        public bool TryCancelUncommittedInteraction(string nodeContentId, out string error)
        {
            error = null;
            LevelNode node = FindNode(nodeContentId);
            if (node?.nodeType != LevelNodeType.Interaction || _activeEncounter.HasValue || _pendingReturn.HasValue ||
                (!string.IsNullOrEmpty(_activeInteractionNodeId) && _activeInteractionNodeId != nodeContentId) ||
                !_nodeStates.TryGetValue(nodeContentId, out var state) ||
                (state != LevelNodeProgress.Available && state != LevelNodeProgress.Resolving) ||
                (state == LevelNodeProgress.Resolving && _activeInteractionNodeId != nodeContentId) ||
                node.completionEffects?.Exists(effect => effect != null && _appliedInteractionEffects.ContainsKey(effect.effectId)) == true)
            { error = "Only the matching unsubmitted interaction can be canceled."; return false; }
            _nodeStates[nodeContentId] = LevelNodeProgress.Available;
            _activeInteractionNodeId = null;
            return true;
        }

        /// <summary>全部配置效果成功登记后完成交互并解锁依赖节点。</summary>
        public bool TryCompleteInteraction(
            string nodeContentId,
            out string error)
        {
            error = null;
            if (_activeInteractionNodeId != nodeContentId)
            {
                error = "没有匹配的活动地图交互。";
                return false;
            }

            LevelNode node = FindNode(nodeContentId);
            if (node == null || !IsNodeEnabled(nodeContentId))
            {
                error = "The current interaction is missing or disabled in this floor contract.";
                return false;
            }
            if (node.completionEffects != null)
            {
                for (int i = 0; i < node.completionEffects.Count; i++)
                {
                    LevelInteractionEffect effect = node.completionEffects[i];
                    if (effect == null ||
                        !_appliedInteractionEffects.ContainsKey(effect.effectId))
                    {
                        error = $"交互效果尚未完成：{effect?.effectId ?? "<null>"}。";
                        return false;
                    }
                }
            }

            _nodeStates[nodeContentId] = LevelNodeProgress.Cleared;
            UnlockDependentNodes();
            _activeInteractionNodeId = null;
            return true;
        }

        [Obsolete("Use TryBeginInteraction.")]
        public bool TryBeginRewardCollection(
            string nodeContentId,
            out string error)
            => TryBeginInteraction(nodeContentId, out error);

        [Obsolete("Use the effect executor followed by TryCompleteInteraction.")]
        public bool TryCompleteRewardCollection(
            string nodeContentId,
            out string error)
        {
            LevelNode node = FindNode(nodeContentId);
            if (node?.completionEffects != null)
            {
                for (int i = 0; i < node.completionEffects.Count; i++)
                {
                    LevelInteractionEffect effect = node.completionEffects[i];
                    if (_appliedInteractionEffects.ContainsKey(effect.effectId))
                        continue;
                    if (!TryApplyRunEffect(
                            nodeContentId,
                            effect,
                            out _,
                            out error))
                        return false;
                }
            }
            return TryCompleteInteraction(nodeContentId, out error);
        }

        /// <summary>确认返回结果。nodeContentId 必须匹配。只能成功一次。</summary>
        public bool TryAcknowledgeReturn(string nodeContentId)
        {
            if (!_pendingReturn.HasValue)
                return false;
            if (_pendingReturn.Value.NodeContentId != nodeContentId)
                return false;
            if (_victorySealRewards.TryGetPending(
                    out LevelVictorySealRewardOffer pending) &&
                string.Equals(
                    pending.NodeContentId,
                    nodeContentId,
                    StringComparison.Ordinal))
                return false;
            _pendingReturn = null;
            _returnConsumed = true;
            return true;
        }

        /// <summary>
        /// Keeps the same floor object when a synchronous conclusion commit
        /// fails after acknowledging its return. Only these two owned fields
        /// participate; encounter, reward and map state are left intact.
        /// </summary>
        internal bool TryCommitReturnAcknowledgement(string nodeContentId, Action commit)
        {
            LevelEncounterReturn? pending = _pendingReturn;
            bool consumed = _returnConsumed;
            if (!TryAcknowledgeReturn(nodeContentId)) return false;
            try { commit?.Invoke(); return true; }
            catch
            {
                _pendingReturn = pending;
                _returnConsumed = consumed;
                throw;
            }
        }

        private static LevelRewardGrant FindVictorySealChoice(LevelNode node)
        {
            if (node?.victoryRewards == null)
                return null;
            for (int i = 0; i < node.victoryRewards.Count; i++)
            {
                LevelRewardGrant grant = node.victoryRewards[i];
                if (grant != null &&
                    grant.kind == LevelRewardKind.SealChoice)
                    return grant;
            }
            return null;
        }

        private static IReadOnlyList<string> GetSealChoiceContentIds(
            LevelRewardGrant grant)
        {
            if (grant?.sealChoiceContentIds != null &&
                grant.sealChoiceContentIds.Count > 0)
            {
                return grant.sealChoiceContentIds;
            }
            return new[] { grant?.targetContentId };
        }

        private bool TryValidatePendingVictorySealReward(
            string rewardContentId,
            out LevelVictorySealRewardOffer pending,
            out string error)
        {
            pending = default;
            error = null;
            if (!_victorySealRewards.TryGetPending(out pending))
            {
                error = "当前没有待处理的胜利灵印奖励。";
                return false;
            }
            if (!string.Equals(
                    pending.RewardContentId,
                    rewardContentId,
                    StringComparison.Ordinal))
            {
                error = $"待处理奖励不匹配：{rewardContentId} / " +
                        $"{pending.RewardContentId}。";
                return false;
            }
            if (!_pendingReturn.HasValue ||
                _pendingReturn.Value.Outcome != CombatOutcome.Victory ||
                !string.Equals(
                    _pendingReturn.Value.NodeContentId,
                    pending.NodeContentId,
                    StringComparison.Ordinal) ||
                !_nodeStates.TryGetValue(
                    pending.NodeContentId,
                    out LevelNodeProgress progress) ||
                progress != LevelNodeProgress.Resolving)
            {
                error = $"胜利奖励 {rewardContentId} 没有匹配的待确认遭遇。";
                return false;
            }
            return true;
        }

        private void CompleteVictorySealRewardNode(string nodeContentId)
        {
            _nodeStates[nodeContentId] = LevelNodeProgress.Cleared;
            UnlockDependentNodes();
        }

        private void UnlockDependentNodes()
        {
            HashSet<string> effectivelyCleared =
                BuildEffectivelyClearedNodeIds();
            IReadOnlyList<LevelNode> effectiveNodes = Nodes;
            for (int i = 0; i < effectiveNodes.Count; i++)
            {
                var node = effectiveNodes[i];
                if (!IsNodeEnabled(node.contentId) || !_nodeStates.TryGetValue(node.contentId, out var progress))
                    continue;
                if (progress != LevelNodeProgress.Locked)
                    continue;

                if (ArePrerequisitesEffectivelyCleared(
                        node, effectivelyCleared))
                    _nodeStates[node.contentId] = LevelNodeProgress.Available;
            }
        }

        /// <summary>
        /// 配置更新可能在已通关节点之间插入新的交互。此时旧存档中的后续
        /// Cleared 不能越过尚未完成的新前置继续解锁；只有整条前置链都成立的
        /// Cleared 才能作为有效解锁依据。
        /// </summary>
        private HashSet<string> BuildEffectivelyClearedNodeIds()
        {
            var result = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            bool added;
            do
            {
                added = false;
                IReadOnlyList<LevelNode> effectiveNodes = Nodes;
                for (int i = 0; i < effectiveNodes.Count; i++)
                {
                    LevelNode node = effectiveNodes[i];
                    if (node == null || !IsNodeEnabled(node.contentId) || result.Contains(node.contentId) ||
                        !_nodeStates.TryGetValue(
                            node.contentId, out LevelNodeProgress progress) ||
                        progress != LevelNodeProgress.Cleared ||
                        !ArePrerequisitesEffectivelyCleared(node, result))
                    {
                        continue;
                    }

                    result.Add(node.contentId);
                    added = true;
                }
            }
            while (added);
            return result;
        }

        private bool ArePrerequisitesEffectivelyCleared(
            LevelNode node,
            ISet<string> effectivelyCleared)
        {
            if (_explorationGuards.TryGetGuard(node.contentId, out string guardId) &&
                !effectivelyCleared.Contains(guardId))
                return false;
            if (node.prerequisiteNodeIds == null ||
                node.prerequisiteNodeIds.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < node.prerequisiteNodeIds.Count; i++)
            {
                if (!effectivelyCleared.Contains(
                        node.prerequisiteNodeIds[i]))
                    return false;
            }
            return true;
        }

        private void ReconcileNodeAvailability()
        {
            HashSet<string> effectivelyCleared =
                BuildEffectivelyClearedNodeIds();
            IReadOnlyList<LevelNode> effectiveNodes = Nodes;
            for (int i = 0; i < effectiveNodes.Count; i++)
            {
                LevelNode node = effectiveNodes[i];
                if (node == null || !_nodeStates.TryGetValue(node.contentId, out LevelNodeProgress progress))
                    continue;
                if (!IsNodeEnabled(node.contentId))
                {
                    if (progress != LevelNodeProgress.Locked)
                        throw new InvalidOperationException("Disabled side-slot progress must remain Locked.");
                    continue;
                }
                if (
                    progress == LevelNodeProgress.Cleared ||
                    progress == LevelNodeProgress.Resolving)
                {
                    continue;
                }

                _nodeStates[node.contentId] =
                    ArePrerequisitesEffectivelyCleared(
                        node, effectivelyCleared)
                        ? LevelNodeProgress.Available
                        : LevelNodeProgress.Locked;
            }
        }

        [Obsolete("Use TryGetPendingReturn + TryAcknowledgeReturn instead.")]
        public bool TryConsumeReturn(out LevelEncounterReturn result)
        {
            if (_pendingReturn.HasValue && !_returnConsumed)
            {
                result = _pendingReturn.Value;
                _returnConsumed = true;
                return true;
            }
            result = default;
            return false;
        }

        /// <summary>恢复中断的遭遇：将未确认的 Resolving 恢复为 Available。</summary>
        public void RecoverInterruptedEncounter()
        {
            if (!string.IsNullOrEmpty(_activeInteractionNodeId))
            {
                string interactionId = _activeInteractionNodeId;
                if (_nodeStates.TryGetValue(
                        interactionId, out var interactionProgress) &&
                    interactionProgress == LevelNodeProgress.Resolving)
                {
                    _nodeStates[interactionId] = IsNodeEnabled(interactionId)
                        ? LevelNodeProgress.Available : LevelNodeProgress.Locked;
                }
                _activeInteractionNodeId = null;
            }

            if (!_activeEncounter.HasValue) return;

            string nodeId = _activeEncounter.Value.NodeContentId;
            if (_nodeStates.TryGetValue(nodeId, out var progress) &&
                progress == LevelNodeProgress.Resolving)
            {
                _nodeStates[nodeId] = IsNodeEnabled(nodeId)
                    ? LevelNodeProgress.Available : LevelNodeProgress.Locked;
            }
            _activeEncounter = null;
        }

        private void RebuildInteractionEffects(LevelSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.nodeStates.Count; i++)
            {
                LevelNodeState nodeState = snapshot.nodeStates[i];
                if (nodeState.progress != LevelNodeProgress.Cleared)
                    continue;
                LevelNode node = FindNode(nodeState.nodeContentId);
                if (node == null ||
                    (node.nodeType != LevelNodeType.Interaction &&
                     node.nodeType != LevelNodeType.Exit) ||
                    node.completionEffects == null)
                    continue;

                for (int effectIndex = 0;
                     effectIndex < node.completionEffects.Count;
                     effectIndex++)
                {
                    LevelInteractionEffect effect =
                        node.completionEffects[effectIndex];
                    if (effect == null ||
                        string.IsNullOrWhiteSpace(effect.effectId) ||
                        _appliedInteractionEffects.ContainsKey(effect.effectId))
                        continue;

                    int before;
                    int after;
                    switch (effect.kind)
                    {
                        case LevelInteractionEffectKind.AddBaseMana:
                            before = _shared.BaseManaBonus;
                            _shared.AddBaseMana(Math.Max(0, effect.amount));
                            after = _shared.BaseManaBonus;
                            break;
                        case LevelInteractionEffectKind.AddHandLimit:
                            before = _shared.HandLimitBonus;
                            _shared.AddHandLimit(Math.Max(0, effect.amount));
                            after = _shared.HandLimitBonus;
                            break;
                        case LevelInteractionEffectKind.AddMight:
                            before = _shared.MightBonus;
                            _shared.AddMight(Math.Max(0, effect.amount));
                            after = _shared.MightBonus;
                            break;
                        case LevelInteractionEffectKind.AddArea:
                            before = _shared.AreaBonus;
                            _shared.AddArea(Math.Max(0, effect.amount));
                            after = _shared.AreaBonus;
                            break;
                        case LevelInteractionEffectKind.AddRewardReroll:
                            before = _shared.RewardRerollsRemaining;
                            _shared.AddRewardRerolls(
                                Math.Max(0, effect.amount));
                            after = _shared.RewardRerollsRemaining;
                            break;
                        case LevelInteractionEffectKind.RecordStoryClue:
                            before = 0;
                            after = Math.Max(0, effect.amount);
                            break;
                        default:
                            // 外部或需要精确对象的效果不能由作者快照
                            // 伪造为已结算。这类节点只能从版本化 Run 存档恢复。
                            continue;
                    }
                    _appliedInteractionEffects.Add(
                        effect.effectId,
                        CreateReceipt(
                            effect, effect.amount, before, after,
                            newlyApplied: false));
                }
            }
        }

        private bool TryValidateConfiguredEffect(
            string nodeContentId,
            LevelInteractionEffect effect,
            out string error)
        {
            error = null;
            if (effect == null || string.IsNullOrWhiteSpace(effect.effectId))
            {
                error = "交互效果及 effectId 不能为空。";
                return false;
            }
            LevelNode node = FindNode(nodeContentId);
            if (node?.completionEffects == null ||
                !node.completionEffects.Exists(candidate =>
                    candidate != null &&
                    candidate.effectId == effect.effectId))
            {
                error = $"节点 {nodeContentId} 未配置交互效果 {effect.effectId}。";
                return false;
            }
            var configured = node.completionEffects.Find(candidate => candidate?.effectId == effect.effectId);
            if (_nodes.HasFrozenInteractions && !_nodes.MatchesEffect(nodeContentId, effect))
            {
                error = "Submitted effect differs from this Run's frozen node contract.";
                return false;
            }
            if (configured.kind == LevelInteractionEffectKind.DuplicateOffering || effect.kind == LevelInteractionEffectKind.DuplicateOffering || effect.HasDuplicateOfferingContract)
            {
                var errors = new List<string>();
                DungeonRunDuplicateOfferings.ValidateEffect(configured, errors);
                DungeonRunDuplicateOfferings.ValidateEffect(effect, errors);
                if (errors.Count != 0 || !DungeonRunDuplicateOfferings.SameContract(configured, effect))
                { error = "DuplicateOffering differs from its complete canonical configured effect."; return false; }
                return true;
            }
            if (configured.kind == LevelInteractionEffectKind.CrawlerInvitation || effect.kind == LevelInteractionEffectKind.CrawlerInvitation || effect.HasCrawlerInvitationContract)
            {
                var errors = new List<string>();
                DungeonRunInvitations.ValidateEffect(configured, errors); DungeonRunInvitations.ValidateEffect(effect, errors);
                if (errors.Count != 0 || !DungeonRunInvitations.SameContract(configured, effect))
                { error = "Invitation differs from its canonical configured effect."; return false; }
                return true;
            }
            if (configured.kind == LevelInteractionEffectKind.GunForHire || effect.kind == LevelInteractionEffectKind.GunForHire || effect.HasGunForHireContract)
            {
                var errors = new List<string>();
                DungeonRunGunForHire.ValidateEffect(configured, errors);
                DungeonRunGunForHire.ValidateEffect(effect, errors);
                if (errors.Count != 0 || !DungeonRunGunForHire.SameContract(configured, effect))
                { error = "GunForHire differs from its complete canonical configured effect."; return false; }
                return true;
            }
            if (configured.kind == LevelInteractionEffectKind.CardStatOffering || effect.kind == LevelInteractionEffectKind.CardStatOffering || effect.HasCardStatOfferingContract)
            {
                var errors = new List<string>();
                DungeonRunCardStatOfferings.ValidateEffect(configured, errors);
                DungeonRunCardStatOfferings.ValidateEffect(effect, errors);
                if (errors.Count != 0 || !DungeonRunCardStatOfferings.SameContract(configured, effect))
                { error = "CardStat Offering differs from its configured complete contract."; return false; }
                return true;
            }
            if (configured.kind == LevelInteractionEffectKind.CrystalThrowing || effect.kind == LevelInteractionEffectKind.CrystalThrowing || effect.HasCrystalThrowingContract)
            {
                var errors = new List<string>();
                CrystalEventContracts.ValidateEffect(configured, errors);
                CrystalEventContracts.ValidateEffect(effect, errors);
                if (errors.Count != 0) { error = string.Join(";", errors); return false; }
                return true;
            }
            if (configured.kind == LevelInteractionEffectKind.AbandonedCart || effect.kind == LevelInteractionEffectKind.AbandonedCart || effect.HasAbandonedCartContract)
            {
                var errors = new List<string>();
                DungeonRunAbandonedCarts.ValidateEffect(configured, errors);
                DungeonRunAbandonedCarts.ValidateEffect(effect, errors);
                if (errors.Count != 0) { error = string.Join("；", errors); return false; }
                return true;
            }
            if (configured.kind == LevelInteractionEffectKind.ChickenOffering || configured.kind == LevelInteractionEffectKind.ManaOffering ||
                effect.kind == LevelInteractionEffectKind.ChickenOffering || effect.kind == LevelInteractionEffectKind.ManaOffering ||
                effect.HasChickenOfferingContract || effect.HasManaOfferingContract)
            {
                var errors = new List<string>();
                DungeonRunChickenOffering.ValidateEffect(configured, errors);
                DungeonRunChickenOffering.ValidateEffect(effect, errors);
                if (configured.kind != effect.kind) errors.Add("Offering payout kind differs from configured effect.");
                if (errors.Count != 0) { error = string.Join("；", errors); return false; }
                return true;
            }
            if (configured.kind == LevelInteractionEffectKind.ManaStatue || effect.kind == LevelInteractionEffectKind.ManaStatue || effect.HasManaStatueContract)
            {
                var errors = new List<string>();
                DungeonRunManaStatues.ValidateEffect(configured, errors);
                DungeonRunManaStatues.ValidateEffect(effect, errors);
                if (errors.Count != 0) { error = string.Join("；", errors); return false; }
                return true;
            }
            if (effect.kind == LevelInteractionEffectKind.ReachLevel ||
                effect.kind == LevelInteractionEffectKind.ChooseArcana || effect.kind == LevelInteractionEffectKind.ChickenOffering || effect.kind == LevelInteractionEffectKind.ManaStatue)
            {
                var errors = new List<string>();
                if (effect.kind == LevelInteractionEffectKind.ManaStatue)
                    DungeonRunManaStatues.ValidateEffect(effect, errors);
                else if (effect.kind == LevelInteractionEffectKind.ChickenOffering)
                    DungeonRunChickenOffering.ValidateEffect(effect, errors);
                else if (effect.kind == LevelInteractionEffectKind.ChooseArcana)
                    DungeonRunArcanas.ValidateEffect(effect, errors);
                else
                    DungeonRunInteractionTransaction.ValidateEffect(effect, errors);
                if (errors.Count == 0)
                    return true;
                error = string.Join("；", errors);
                return false;
            }
            if (effect.amount <= 0)
            {
                error = $"交互效果 {effect.effectId} 的 amount 必须大于 0。";
                return false;
            }
            return true;
        }

        private static LevelInteractionReceipt CreateReceipt(
            LevelInteractionEffect effect,
            int amount,
            int valueBefore,
            int valueAfter,
            bool newlyApplied,
            int targetRuntimeInstanceId = 0)
        {
            return new LevelInteractionReceipt(
                effect.effectId,
                effect.kind,
                effect.targetContentId,
                effect.presentationId,
                amount,
                valueBefore,
                valueAfter,
                newlyApplied,
                effect.summaryText,
                effect.detailText,
                effect.rarity,
                targetRuntimeInstanceId);
        }

        private static LevelInteractionReceipt AsRetryReceipt(
            LevelInteractionReceipt receipt)
        {
            return new LevelInteractionReceipt(
                receipt.EffectId,
                receipt.Kind,
                receipt.TargetContentId,
                receipt.PresentationId,
                receipt.Amount,
                receipt.ValueBefore,
                receipt.ValueAfter,
                newlyApplied: false,
                receipt.SummaryText,
                receipt.DetailText,
                receipt.Rarity,
                receipt.TargetRuntimeInstanceId);
        }

        internal LevelRunSaveData CaptureSaveData()
        {
            return LevelRunSaveCodec.Capture(new LevelRunSaveSource
            {
                ProfileContentId = Profile.contentId,
                HasFrozenInteractionContract = _nodes.HasFrozenInteractions,
                FrozenInteractionContractFingerprint = _nodes.Fingerprint,
                FrozenNodeDirectoryJson = _nodes.HasFrozenInteractions
                    ? LevelRunNodeDirectoryCheckpoint.Capture(_nodes).CanonicalJson : string.Empty,
                SnapshotId = _snapshotId,
                NodeStates = _nodeStates,
                InteractionReceipts = _appliedInteractionEffects,
                ActiveEncounter = _activeEncounter,
                ActiveInteractionNodeContentId = _activeInteractionNodeId,
                PendingReturn = _pendingReturn,
                ReturnConsumed = _returnConsumed,
                EncounterCount = _encounterCount,
                VictoryCount = _victoryCount,
                VictorySealRewards =
                    _victorySealRewards.CaptureSaveData(),
                CompletedPresentationIds =
                    Presentations.CaptureCompletedIds()
            });
        }

        internal static LevelRunSession Restore(
            LevelRunProfile profile,
            LevelRunSaveData data,
            string runId,
            DungeonRunSharedState shared,
            LevelExplorationGuardView explorationGuards = null,
            LevelRunNodeDirectory nodeDirectory = null)
        {
            nodeDirectory = LevelRunSaveCodec.RestoreDirectory(profile, data, nodeDirectory);
            LevelRunRestoredState restored =
                LevelRunSaveCodec.Restore(profile, data, runId, nodeDirectory);
            var session = new LevelRunSession(
                profile,
                data.snapshotId,
                runId,
                shared,
                rebuildInteractionEffects: false,
                explorationGuards: explorationGuards,
                nodeDirectory: nodeDirectory);
            session._nodeStates.Clear();
            foreach (KeyValuePair<string, LevelNodeProgress> item in
                     restored.NodeStates)
                session._nodeStates.Add(item.Key, item.Value);
            session._appliedInteractionEffects.Clear();
            foreach (KeyValuePair<string, LevelInteractionReceipt> item in
                     restored.InteractionReceipts)
                session._appliedInteractionEffects.Add(item.Key, item.Value);
            session._activeEncounter = restored.ActiveEncounter;
            session._activeInteractionNodeId =
                restored.ActiveInteractionNodeContentId;
            session._pendingReturn = restored.PendingReturn;
            session._returnConsumed = restored.ReturnConsumed;
            session._encounterCount = restored.EncounterCount;
            session._victoryCount = restored.VictoryCount;
            session._victorySealRewards = restored.VictorySealRewards ??
                new LevelVictorySealRewardStateMachine();
            session.Presentations.RestoreCompletedIds(
                restored.CompletedPresentationIds);
            session.ReconcileNodeAvailability();
            return session;
        }

        /// <summary>Exact detached transaction copy: no snapshot rewards or availability reconciliation are replayed.</summary>
        // An interaction save owns deck/progression separately. This snapshot
        // retains the floor's receipts and lifecycle for an atomic publication.
        internal LevelRunSession CaptureTransactionState()
            => ForkForFloorExit(_shared.CloneForFloorPreparation());

        internal LevelRunSession ForkForFloorExit(DungeonRunSharedState shared)
        {
            var draft = new LevelRunSession(Profile, _snapshotId, RunId, shared,
                rebuildInteractionEffects: false, explorationGuards: _explorationGuards,
                nodeDirectory: _nodes);
            draft.AdoptPreparedState(this);
            return draft;
        }

        /// <summary>Prepare an idle same-floor contract publication without replaying rewards.
        /// Source placement proof is validated by the Run owner before adoption.</summary>
        internal LevelRunSession PrepareDirectoryTransition(LevelRunNodeDirectory directory)
        {
            RequireFloorExitReady();
            directory.RequireMatches(Profile);
            LevelRunSaveData data = CaptureSaveData();
            data.hasNodeDirectoryCheckpointState = true;
            data.hasFrozenInteractionContract = directory.HasFrozenInteractions;
            data.frozenInteractionContractFingerprint = directory.Fingerprint;
            data.frozenNodeDirectoryJson = LevelRunNodeDirectoryCheckpoint.Capture(directory).CanonicalJson;
            foreach (var state in data.nodeStates)
                if (!directory.IsNodeEnabled(state.nodeContentId))
                {
                    if (state.progress != LevelNodeProgress.Locked && state.progress != LevelNodeProgress.Available)
                        throw new InvalidOperationException("Source publication cannot disable an executed node.");
                    state.progress = LevelNodeProgress.Locked;
                }
            return Restore(Profile, data, RunId, _shared, _explorationGuards, directory);
        }

        /// <summary>The expected old immutable directory is a stale-publication token.
        /// Ordinary transactions keep their existing same-directory adoption contract.</summary>
        internal void AdoptPreparedDirectoryState(LevelRunSession prepared, LevelRunNodeDirectory expectedDirectory)
        {
            if (_nodes != expectedDirectory || prepared == null || prepared.Profile != Profile ||
                prepared.RunId != RunId || prepared._snapshotId != _snapshotId ||
                prepared._explorationGuards != _explorationGuards)
                throw new InvalidOperationException("Prepared source publication is stale or belongs to another floor.");
            var prior = _nodes;
            _nodes = prepared._nodes;
            try { AdoptPreparedState(prepared); }
            catch { _nodes = prior; throw; }
        }

        /// <summary>
        /// A synchronous floor-exit commit/rollback keeps the old map's existing
        /// session identity valid. Only a fully prepared same-profile state may
        /// be adopted; no shared progression, random decisions or events change.
        /// </summary>
        internal void AdoptPreparedState(LevelRunSession prepared)
        {
            if (prepared == null || prepared.Profile != Profile ||
                prepared.RunId != RunId || prepared._snapshotId != _snapshotId ||
                prepared._explorationGuards != _explorationGuards ||
                prepared._nodes != _nodes)
                throw new InvalidOperationException("Prepared floor state belongs to another floor or Run.");
            _nodeStates.Clear();
            foreach (var item in prepared._nodeStates) _nodeStates.Add(item.Key, item.Value);
            _appliedInteractionEffects.Clear();
            foreach (var item in prepared._appliedInteractionEffects) _appliedInteractionEffects.Add(item.Key, item.Value);
            _activeEncounter = prepared._activeEncounter;
            _activeInteractionNodeId = prepared._activeInteractionNodeId;
            _pendingReturn = prepared._pendingReturn;
            _returnConsumed = prepared._returnConsumed;
            _encounterCount = prepared._encounterCount;
            _victoryCount = prepared._victoryCount;
            _victorySealRewards = LevelVictorySealRewardStateMachine.Restore(
                prepared._victorySealRewards.CaptureSaveData());
            Presentations.RestoreCompletedIds(prepared.Presentations.CaptureCompletedIds());
        }

        /// <summary>中止活动遭遇（如 scenario 缺失）。恢复节点为 Available。</summary>
        public void AbortActiveEncounter(string reason)
        {
            if (!_activeEncounter.HasValue)
                return;

            string nodeId = _activeEncounter.Value.NodeContentId;
            if (_nodeStates.TryGetValue(nodeId, out var progress) &&
                progress == LevelNodeProgress.Resolving)
            {
                _nodeStates[nodeId] = IsNodeEnabled(nodeId)
                    ? LevelNodeProgress.Available : LevelNodeProgress.Locked;
            }
            _activeEncounter = null;
        }
    }

}
