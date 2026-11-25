using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 地图交互唯一提交边界：选择键校验、实例变更、经验检查点、经济回滚和终态回执
    /// 均在这里完成。每次操作先在深拷贝上准备；没有经过提交的预览不修改 Run 或随机流。
    /// UI 与模拟器共用此入口，持久化仅保存纯数据，不保存面板或回调。
    /// 规模审查：超过 800 行但仍独占同一事务的检查点/提交状态机，不按时间段拆分。
    /// 升级候选协议、自然抽样接点与宿主合法性由 DungeonRunNaturalLevelRewardRules
    /// 隐藏；它只准备草稿，不持有 Run。新增独立事务或第二个状态 owner 时重审。
    /// </summary>
    public sealed class DungeonRunInteractionTransaction
    {
        private readonly DungeonRunSession _run;
        private DungeonRunInteractionSaveData _state;
        private bool _committing;

        internal DungeonRunInteractionTransaction(DungeonRunSession run, DungeonRunInteractionSaveData state = null)
        {
            _run = run;
            _state = Clone(state ?? new DungeonRunInteractionSaveData());
            ValidateRestoredState();
        }

        internal bool HasOwnPendingChoice => !string.IsNullOrWhiteSpace(_state.effectId);
        public bool HasPendingChoice => HasOwnPendingChoice || _run.GemChests?.HasPendingChoice == true;
        internal bool IsCommitting => _committing;

        /// <summary>Run 准入只读取待选所属节点，不暴露可变检查点或适配器本地状态。</summary>
        public string PendingNodeContentId => HasOwnPendingChoice ? _state.nodeContentId : _run.GemChests?.PendingNodeContentId;

        /// <summary>
        /// Commits one canonical map recovery effect, its pickup fact and receipt in a
        /// strict save, including full-health pickups with zero actual healing.
        /// Saved receipts replay without healing, counting another pickup or saving.
        /// Persist must atomically write or throw before committing any bytes. This API
        /// intentionally leaves node completion to the existing map flow. In a
        /// multi-effect node, only this healing effect is atomic, not the whole node.
        /// Scale review: recovery is a targetless command within this existing map
        /// transaction owner. No offer state, separate lifecycle or second owner is added;
        /// reassess this boundary before adding another reward-selection lifecycle.
        /// </summary>
        public bool TryHeal(string nodeId, string effectId, out LevelInteractionReceipt receipt,
            out string error, Action persist = null)
        {
            receipt = default; error = null;
            // Check all six owners before historical receipts, including broken-wallet recovery.
            if (_run.IsRewardCommitInProgress)
            { error = "地图回血事务正在提交，或另一奖励事务尚未恢复。"; return false; }
            LevelRunSession floor = _run.CurrentFloor;
            LevelNode node = floor.FindNode(nodeId);
            LevelInteractionEffect effect = node?.completionEffects?.Find(item =>
                item != null && item.effectId == effectId);
            if (node == null || (node.nodeType != LevelNodeType.Interaction && node.nodeType != LevelNodeType.Exit) ||
                string.IsNullOrWhiteSpace(effectId) || effect == null ||
                effect.kind != LevelInteractionEffectKind.HealCurrentHealth || effect.amount <= 0 || effect.amountMax != 0 ||
                !string.IsNullOrEmpty(effect.targetContentId))
            { error = "节点未配置合法的固定回血效果。"; return false; }
            if (_run.TryGetConclusion(out _) || HasPendingChoice || (_run.Arcanas.HasPendingChoice || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy)) ||
                _run.GetNodeAdmission(nodeId) == DungeonRunNodeAdmission.PendingChoiceOutstanding)
            { error = "请先完成当前 Run 的待处理奖励。"; return false; }
            if (floor.TryGetInteractionReceipt(effectId, out var prior))
            {
                if (prior.Kind != effect.kind || prior.TargetRuntimeInstanceId != 0 ||
                    !string.Equals(prior.TargetContentId ?? string.Empty, effect.targetContentId ?? string.Empty, StringComparison.Ordinal) ||
                    !string.Equals(prior.PresentationId ?? string.Empty, effect.presentationId ?? string.Empty, StringComparison.Ordinal) ||
                    prior.ValueBefore < 0 || prior.ValueAfter < prior.ValueBefore ||
                    prior.Amount != prior.ValueAfter - prior.ValueBefore || prior.Amount > effect.amount)
                { error = "已保存的回血回执与当前配置不一致。"; return false; }
                receipt = prior;
                return true;
            }
            if (floor.ActiveInteractionNodeId != nodeId || floor.GetNodeState(nodeId) != LevelNodeViewState.Completing)
            { error = "没有匹配的活动地图回血交互。"; return false; }
            if (persist == null)
            { error = "地图回血首次提交需要严格保存回调。"; return false; }
            int requested = effect.amount, maximum = _run.MaxHealth;
            LevelInteractionReceipt applied = default;
            _committing = true;
            try
            {
                _run.CommitRewardState(() =>
                {
                    if (!_run.ApplyHealing(requested, out int before, out int after, out string applyError) ||
                        !floor.TryRecordExternalEffect(nodeId, effect, before, after, after - before,
                            out applied, out applyError))
                        throw new InvalidOperationException(applyError);
                    RequireHealingCheckpoint();
                }, () =>
                {
                    // Persistence may inspect the staged projection but cannot complete/replace its node.
                    persist();
                    RequireHealingCheckpoint();
                });
                receipt = applied;
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally { _committing = false; }

            void RequireHealingCheckpoint()
            {
                if (!ReferenceEquals(_run.CurrentFloor, floor) || floor.ActiveInteractionNodeId != nodeId ||
                    floor.GetNodeState(nodeId) != LevelNodeViewState.Completing || floor.ActiveEncounter.HasValue ||
                    floor.TryGetPendingReturn(out _) || _run.CurrentHealth != applied.ValueAfter ||
                    _run.MaxHealth != maximum || !floor.MatchesConfiguredEffect(nodeId, effect) ||
                    !floor.TryGetInteractionReceipt(effectId, out var stored) || stored.Kind != applied.Kind ||
                    stored.Amount != applied.Amount || stored.ValueBefore != applied.ValueBefore ||
                    stored.ValueAfter != applied.ValueAfter || stored.TargetRuntimeInstanceId != 0)
                    throw new InvalidOperationException("地图回血提交期间的节点、配置或玩家状态已变化。");
            }
        }

        public static bool Handles(LevelInteractionEffectKind kind)
            => kind == LevelInteractionEffectKind.GemChest || kind == LevelInteractionEffectKind.GrantCard || kind == LevelInteractionEffectKind.GrantExperience ||
               kind == LevelInteractionEffectKind.RefineCard || kind == LevelInteractionEffectKind.ChooseSeal ||
               kind == LevelInteractionEffectKind.ReachLevel || kind == LevelInteractionEffectKind.ChooseCard;

        public static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        {
            if (effect.HasCrawlerInvitationContract) errors.Add("Free invitation requires its dedicated lifecycle and cannot mix with a generic reward.");
            if (effect.kind == LevelInteractionEffectKind.GemChest) { DungeonRunGemChests.ValidateEffect(effect, errors); return; }
            if (effect.kind == LevelInteractionEffectKind.CrystalThrowing) { CrystalEventContracts.ValidateEffect(effect, errors); return; }
            if (effect.HasDoubleKeyPayment) effect.doubleKeyPayment.Validate(effect, errors);
            if (effect.kind == LevelInteractionEffectKind.ReachLevel)
            {
                if (effect.amount != 0 || effect.amountMax != 0)
                {
                    errors.Add(
                        $"目标等级交互 {effect.effectId} 不得配置推测的 amount 或 amountMax。");
                }
                if (!effect.HasTargetLevelResultContract)
                {
                    errors.Add($"目标等级交互 {effect.effectId} 缺少参考结果合同。");
                }
                else
                {
                    effect.targetLevelResult.Validate(effect.effectId, errors);
                }
            }
            else
            {
                if (effect.amount <= 0 || effect.amountMax != 0)
                    errors.Add($"交互事务 {effect.effectId} 必须配置正数固定 amount，不允许 amountMax。");
                bool randomSealOffer = effect.kind == LevelInteractionEffectKind.ChooseSeal &&
                    !string.IsNullOrWhiteSpace(effect.sealRewardPoolId);
                if (effect.kind != LevelInteractionEffectKind.GrantExperience &&
                    !randomSealOffer && effect.amount != 1)
                    errors.Add($"交互事务 {effect.effectId} 每次只能处理一个卡牌、配方或灵印。");
            }
            if (effect.kind == LevelInteractionEffectKind.RefineCard &&
                effect.HasFixedEvolutionContract)
            {
                effect.fixedEvolution.Validate(effect.effectId, errors);
            }
            if (effect.kind != LevelInteractionEffectKind.RefineCard &&
                effect.HasFixedEvolutionContract)
            {
                errors.Add(
                    $"交互事务 {effect.effectId} 只有 RefineCard 可以配置 fixedEvolution。");
            }
            if (effect.kind != LevelInteractionEffectKind.ReachLevel &&
                effect.HasTargetLevelResultContract)
            {
                errors.Add(
                    $"交互事务 {effect.effectId} 只有 ReachLevel 可以配置 targetLevelResult。");
            }
            if (effect.kind == LevelInteractionEffectKind.GrantCard && string.IsNullOrWhiteSpace(effect.targetContentId))
                errors.Add($"发卡效果 {effect.effectId} 必须引用稳定卡牌 contentId。");
            if (effect.kind == LevelInteractionEffectKind.ChooseCard)
            {
                var cardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (effect.cardChoices == null || effect.cardChoices.Count != 4)
                {
                    errors.Add($"选卡效果 {effect.effectId} 必须恰好配置 4 个固定卡牌候选。");
                }
                foreach (string cardContentId in effect.cardChoices ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(cardContentId) || !cardIds.Add(cardContentId))
                    {
                        errors.Add($"选卡效果 {effect.effectId} 包含空或重复的稳定卡牌 contentId。");
                    }
                }
            }
            else if (effect.cardChoices != null && effect.cardChoices.Count > 0)
            {
                errors.Add($"交互效果 {effect.effectId} 只有 ChooseCard 可以配置固定卡牌候选。");
            }
            if (effect.cashOutJade < 0 || (effect.kind != LevelInteractionEffectKind.ChooseSeal && effect.cashOutJade != 0))
                errors.Add($"交互效果 {effect.effectId} 的化玉配置不合法。");
            if (effect.kind != LevelInteractionEffectKind.ChooseSeal)
            {
                if (!string.IsNullOrWhiteSpace(effect.sealRewardPoolId))
                    errors.Add($"交互效果 {effect.effectId} 只有 ChooseSeal 可以配置随机灵印池。");
                return;
            }
            bool usesRandomPool = !string.IsNullOrWhiteSpace(effect.sealRewardPoolId);
            if (usesRandomPool && effect.sealChoices != null && effect.sealChoices.Count > 0)
                errors.Add($"选印效果 {effect.effectId} 的随机灵印池与固定候选不能同时配置。");
            if (usesRandomPool)
                return;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (effect.sealChoices == null || effect.sealChoices.Count == 0)
                errors.Add($"选印效果 {effect.effectId} 缺少固定候选。");
            foreach (CardSealSpec seal in effect.sealChoices ?? new List<CardSealSpec>())
                if (seal == null || string.IsNullOrWhiteSpace(seal.contentId) || !ids.Add(seal.contentId) ||
                    !Enum.IsDefined(typeof(CardSealEffect), seal.effect) || seal.value <= 0)
                    errors.Add($"选印效果 {effect.effectId} 包含无效或重复灵印。");
        }

        public bool TryInspect(string nodeId, DungeonRunInteractionCatalog catalog,
            out DungeonRunInteractionOffer offer, out string error)
        {
            catalog = catalog?.RestrictSealPermission(_run.CanReceiveSealRewards);
            if (IsGemChestNode(nodeId)) return _run.GemChests.TryInspect(nodeId, catalog, out offer, out error);
            offer = null;
            if (!TryRequireEffect(nodeId, catalog, out LevelInteractionEffect effect, out error)) return false;
            if (_run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out var receipt))
            {
                offer = Completed(effect, receipt);
                return true;
            }
            if (HasPendingChoice && _state.effectId != effect.effectId)
            {
                error = "请先完成上一项地图奖励选择。";
                return false;
            }
            try
            {
                offer = BuildOffer(effect, catalog, _run.CaptureInteractionDraft(), _state);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        /// <summary>
        /// choiceId 只能来自最近读取的候选；空键启动自动效果。旧检查点的重复点击
        /// 不重复发奖，目标过期则拒绝。成功后调用方应立即保存，再播放表现。
        /// </summary>
        public bool TryCommit(string nodeId, string choiceId, DungeonRunInteractionCatalog catalog,
            out DungeonRunInteractionOffer offer, out string error, Action persist = null)
        {
            catalog = catalog?.RestrictSealPermission(_run.CanReceiveSealRewards);
            if (IsGemChestNode(nodeId)) return _run.GemChests.TryCommit(nodeId, choiceId, catalog, out offer, out error, persist);
            offer = null;
            error = null;
            if (_committing || _run.VictorySealRewards.IsCommitting || _run.FloorExits.IsCommitting || _run.CardConversions.IsCommitting || _run.SideEvents.IsCommitting || _run.Crystals.IsCommitting || (_run.Arcanas.IsCommitting || (_run.ChickenOfferings.IsCommitting || _run.ManaStatues.IsCommitting || _run.AbandonedCarts.IsCommitting) || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy)))
            { error = "地图事务正在提交。"; return false; }
            if (!TryRequireEffect(nodeId, catalog, out LevelInteractionEffect effect, out error)) return false;
            if (effect.kind == LevelInteractionEffectKind.CrystalThrowing && persist == null)
            { error = "Crystal rewards require strict persistence."; return false; }
            choiceId ??= string.Empty;
            if (_run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out var prior))
            {
                var decision = _state.completed.Find(item => item.effectId == effect.effectId);
                if (choiceId.Length > 0 && decision != null && decision.choiceId != choiceId)
                { error = "此交互已按另一选择结算。"; return false; }
                offer = Completed(effect, prior);
                return true;
            }
            if (_run.CurrentFloor.ActiveInteractionNodeId != nodeId)
            { error = "没有匹配的活动地图交互。"; return false; }
            if (HasPendingChoice && _state.effectId != effect.effectId)
            { error = "请先完成上一项地图奖励选择。"; return false; }
            if (choiceId.Length > 0 && _state.committedChoiceIds.Contains(choiceId))
                return TryInspect(nodeId, catalog, out offer, out error);

            _committing = true;
            try
            {
                var draft = _run.CaptureInteractionDraft();
                var next = Clone(_state);
                var current = BuildOffer(effect, catalog, draft, next);
                if (current.Phase == DungeonRunInteractionPhase.Unavailable)
                { error = current.Message; offer = current; return false; }
                DungeonRunInteractionOption selected = current.Options.FirstOrDefault(item => item.Id == choiceId);
                if (current.Phase != DungeonRunInteractionPhase.Ready && selected == null &&
                    !(current.Phase == DungeonRunInteractionPhase.SealTarget && choiceId == "back"))
                { error = "选择已过期或不属于当前交互候选。"; return false; }
                if (current.Phase == DungeonRunInteractionPhase.Ready && choiceId.Length != 0)
                { error = "此交互不接受选择目标。"; return false; }

                bool isLevelReward = effect.kind == LevelInteractionEffectKind.GrantExperience ||
                    effect.kind == LevelInteractionEffectKind.ReachLevel || effect.kind == LevelInteractionEffectKind.CrystalThrowing;
                if (isLevelReward && next.naturalRewardVersion == 1 &&
                    (choiceId == "back" ||
                     (current.Phase == DungeonRunInteractionPhase.RewardChoice && selected?.Seal != null)))
                {
                    if (choiceId == "back") next.selectedSealContentId = null;
                    else DungeonRunNaturalLevelRewardRules.SelectSeal(catalog, draft, next, selected);
                    return CommitCheckpoint(effect, catalog, draft, next, out offer, persist);
                }

                int before = draft.Cards.Count, after = before, amount = 1, targetId = 0;
                string targetContentId = effect.targetContentId;
                switch (effect.kind)
                {
                    case LevelInteractionEffectKind.GrantCard:
                        targetId = AddCard(draft, effect.targetContentId, catalog);
                        after = draft.Cards.Count;
                        break;
                    case LevelInteractionEffectKind.ChooseCard:
                        targetId = AddCard(draft, selected.CardContentId, catalog);
                        targetContentId = selected.CardContentId;
                        after = draft.Cards.Count;
                        break;
                    case LevelInteractionEffectKind.CrystalThrowing:
                        if (!AdvanceExperience(nodeId, effect, selected, catalog, draft, next, out error)) return false;
                        if (DungeonRunNaturalLevelRewardRules.HasChoices(next))
                            return CommitCheckpoint(effect, catalog, draft, next, out offer, persist);
                        return CommitCrystalRewardCheckpoint(draft, next, out offer, persist);
                    case LevelInteractionEffectKind.GrantExperience:
                        if (!AdvanceExperience(nodeId, effect, selected, catalog, draft, next, out error)) return false;
                        if (DungeonRunNaturalLevelRewardRules.HasChoices(next))
                            return CommitCheckpoint(effect, catalog, draft, next, out offer, persist);
                        before = next.experienceBefore;
                        amount = next.settledExperience;
                        after = checked(before + amount);
                        break;
                    case LevelInteractionEffectKind.ReachLevel:
                        if (!AdvanceToTargetLevel(nodeId, effect, selected, catalog, draft, next, out error)) return false;
                        if (DungeonRunNaturalLevelRewardRules.HasChoices(next))
                            return CommitCheckpoint(effect, catalog, draft, next, out offer, persist);
                        before = next.targetLevelBefore;
                        after = next.targetLevelAfter;
                        amount = Math.Max(0, after - before);
                        targetContentId = effect.targetLevelResult.referenceLoadoutContentId;
                        break;
                    case LevelInteractionEffectKind.RefineCard:
                        DungeonRunEvolutionRules.Apply(draft, selected, catalog);
                        targetId = selected.CardRuntimeInstanceId;
                        targetContentId = selected.CardContentId;
                        after = draft.Cards.Count;
                        break;
                    case LevelInteractionEffectKind.ChooseSeal:
                        if (choiceId == "cash-out")
                            return CommitCashOut(nodeId, effect, choiceId, catalog, draft, next, out offer, out error, persist);
                        if (UsesRandomSealPool(effect) && next.rewardSealIds.Count == 0)
                        {
                            if (!TryFreezeRandomSealChoices(effect, catalog, next, out error))
                                return false;
                            next.nodeContentId = nodeId;
                            next.effectId = effect.effectId;
                            return CommitCheckpoint(effect, catalog, draft, next, out offer, persist);
                        }
                        if (current.Phase == DungeonRunInteractionPhase.SealChoice || choiceId == "back")
                        {
                            next.nodeContentId = nodeId;
                            next.effectId = effect.effectId;
                            next.selectedSealContentId = choiceId == "back" ? null : selected.Seal.contentId;
                            return CommitCheckpoint(effect, catalog, draft, next, out offer, persist);
                        }
                        var saved = draft.Cards.Find(card => card.runtimeInstanceId == selected.CardRuntimeInstanceId);
                        if (!DungeonRunSealAssignmentRules.TryPrepare(saved, catalog.FindCard(saved.contentId),
                                current.SelectedSeal, catalog.CanEvolveTo, out var replacement,
                                out int assignedSlot, out error,
                                references: DungeonRunCardCodec.CreateResolver(catalog.Cards))) return false;
                        before = assignedSlot;
                        after = assignedSlot + 1;
                        draft.Cards[draft.Cards.IndexOf(saved)] = replacement;
                        targetId = saved.runtimeInstanceId;
                        targetContentId = current.SelectedSeal.contentId;
                        draft.ForcedOpeningCardId = targetId;
                        break;
                }
                return CommitTerminal(nodeId, effect, choiceId, draft, next,
                    before, after, amount, targetId, targetContentId, out offer, out error, persist);
            }
            catch (Exception exception) { error = "地图交互提交失败：" + exception.Message; return false; }
            finally { _committing = false; }
        }

        internal DungeonRunInteractionSaveData CaptureSaveData() => Clone(_state);

        private bool IsCrystalCheckpoint(LevelInteractionEffect effect)
            => effect?.kind == LevelInteractionEffectKind.CrystalThrowing && HasPendingChoice &&
               _state.experienceOrigin == 1 && _state.effectId == effect.effectId;

        // Only the currently committing Crystal owner may prepare already-adjusted
        // XP. This same owner retains every natural card/seal reward checkpoint.
        internal void StageCrystalHit(DungeonRunCrystalExperienceCommand command, DungeonRunInteractionCatalog catalog)
        {
            if (command == null || !ReferenceEquals(command.Owner, _run.Crystals) || _committing || HasPendingChoice)
                throw new InvalidOperationException("Crystal cannot overlap another map reward checkpoint.");
            command.Owner.RequirePreparedCommand(command);
            CrystalEventContracts.GetSpecification(command.Effect);
            if (catalog == null || command.AdjustedExperience < 0)
                throw new InvalidOperationException("Crystal requires real content and nonnegative settled experience.");
            var draft = _run.CaptureInteractionDraft();
            var next = new DungeonRunInteractionSaveData {
                completed = Clone(_state).completed, nodeContentId = command.NodeContentId,
                effectId = command.Effect.effectId, experienceStarted = true, experienceOrigin = 1,
                crystalHitOrdinal = command.HitOrdinal, crystalCardRuntimeInstanceId = command.CardRuntimeInstanceId,
                crystalAdjustedExperience = command.AdjustedExperience,
                experienceBefore = draft.Progression.CurrentExperience, settledExperience = command.AdjustedExperience,
                // Levels only advance in this event; keys and RNG seeds must not
                // repeat at rewardIndex=1 on a later physical hit of the same node.
                rewardIndex = draft.Progression.CurrentLevel
            };
            // Growth is already applied once. Store preserves the shared fraction carry.
            draft.Progression.Store(draft.Progression.CurrentLevel,
                checked(draft.Progression.CurrentExperience + command.AdjustedExperience));
            draft.ExperienceCollections.Record(_run.CurrentFloorContentId,
                new ExperienceCollectionTotals(command.AdjustedExperience, command.AdjustedExperience));
            int required = draft.Progression.GetRequiredExperience(draft.Progression.CurrentLevel);
            if (draft.Progression.CurrentExperience >= required)
            {
                draft.Progression.Store(checked(draft.Progression.CurrentLevel + 1), draft.Progression.CurrentExperience - required);
                next.rewardIndex++;
                BuildRewardChoices(command.Effect, catalog, draft, next, draft.Progression.CurrentLevel, false, out _);
            }
            _run.CommitInteractionDraft(draft);
            _state = DungeonRunNaturalLevelRewardRules.HasChoices(next) ? next :
                new DungeonRunInteractionSaveData { completed = next.completed };
        }

        internal void RestoreCrystalCheckpoint(DungeonRunInteractionSaveData previous) => _state = Clone(previous);

        internal void RequireCrystalRewardCheckpoint(DungeonRunCrystalEvents owner)
        {
            if (!ReferenceEquals(owner, _run.Crystals)) throw new InvalidOperationException("Wrong Crystal owner.");
            if (_state.experienceOrigin != 0 && _state.experienceOrigin != 1)
                throw new InvalidOperationException("Unknown map experience origin.");
            if (_state.experienceOrigin == 0)
            {
                if (_state.crystalHitOrdinal != 0 || _state.crystalCardRuntimeInstanceId != 0 || _state.crystalAdjustedExperience != 0 ||
                    (owner.HasPendingEvent && HasPendingChoice))
                    throw new InvalidOperationException("Unproven or overlapping Crystal reward checkpoint.");
                return;
            }
            if (!HasPendingChoice || !owner.HasPendingEvent || owner.PendingNodeContentId != _state.nodeContentId ||
                !owner.TryGetCommittedHit(_state.effectId, _state.crystalCardRuntimeInstanceId, out var hit) ||
                hit.NodeContentId != _state.nodeContentId || hit.FloorContentId != _run.CurrentFloorContentId ||
                hit.HitOrdinal != _state.crystalHitOrdinal || hit.ActualExperience != _state.crystalAdjustedExperience ||
                _state.settledExperience != hit.ActualExperience ||
                _state.rewardReachedLevel != _run.CombatProgression.CurrentLevel ||
                _state.rewardIndex != _state.rewardReachedLevel)
                throw new InvalidOperationException("Crystal reward has no matching durable physical hit proof.");
        }

        private bool CommitCrystalRewardCheckpoint(DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData next,
            out DungeonRunInteractionOffer offer, Action persist)
        {
            if (persist == null) throw new InvalidOperationException("Crystal rewards require strict persistence.");
            var previous = _state;
            try { CommitCrystalChoiceDraft(draft,
                new DungeonRunInteractionSaveData { completed = next.completed }, persist); }
            catch { _state = previous; throw; }
            // Completed means this hit's rewards drained, not the Crystal node finished.
            offer = new DungeonRunInteractionOffer { Phase = DungeonRunInteractionPhase.Completed };
            return true;
        }

        // Crystal keeps its node active while one hit's natural choices drain.
        // The aggregate owns rollback; serialized state and the frozen contract
        // must still match after an external strict-save callback returns.
        private void CommitCrystalChoiceDraft(DungeonRunInteractionDraft draft,
            DungeonRunInteractionSaveData next, Action persist)
        {
            if (persist == null) throw new InvalidOperationException("Crystal rewards require strict persistence.");
            LevelRunSession floor = _run.CurrentFloor;
            string nodeId = _run.Crystals.PendingNodeContentId;
            LevelInteractionEffect effect = floor.FindNode(nodeId)?.completionEffects?[0];
            CrystalEventContracts.GetSpecification(effect);
            _run.CommitRewardState(() => {
                _run.CommitInteractionDraft(draft); _state = next;
                _run.Crystals.RequireRewardCheckpoint();
            }, () => {
                string expected = _run.SerializeForDeterministicComparison();
                persist();
                if (!ReferenceEquals(floor, _run.CurrentFloor) || !ReferenceEquals(_state, next) ||
                    !floor.MatchesConfiguredEffect(nodeId, effect) || expected != _run.SerializeForDeterministicComparison())
                    throw new InvalidOperationException("Crystal reward changed during strict persistence.");
                _run.Crystals.RequireRewardCheckpoint();
            });
        }


        // Payment owns eligibility/removal; this owner only opens its ordinary
        // fixed-seal checkpoint. The payment boundary restores it if save fails.
        internal void OpenPaidSealChoice(string nodeId, string effectId)
        {
            if (HasPendingChoice || _committing)
                throw new InvalidOperationException("已有地图选择待完成。");
            _state = new DungeonRunInteractionSaveData
                { nodeContentId = nodeId, effectId = effectId, completed = _state.completed };
        }

        internal void RestorePaymentCheckpoint(DungeonRunInteractionSaveData previous) => _state = previous;

        // Read-only compatibility facade; all Gem state and decisions belong to GemChests.
        private bool IsGemChestNode(string nodeId)
            => _run.CurrentFloor.FindNode(nodeId)?.completionEffects?.Count == 1 &&
               _run.CurrentFloor.FindNode(nodeId).completionEffects[0]?.kind == LevelInteractionEffectKind.GemChest;

        private bool TryRequireEffect(string nodeId, DungeonRunInteractionCatalog catalog,
            out LevelInteractionEffect effect, out string error)
        {
            effect = null;
            error = null;
            if (_run.DoubleKeyPayments.IsCommitting || _run.VictorySealRewards.IsCommitting ||
                _run.FloorExits.IsCommitting || _run.CardConversions.IsCommitting || _run.SideEvents.IsCommitting || _run.Crystals.IsCommitting || (_run.Arcanas.IsCommitting || (_run.ChickenOfferings.IsCommitting || _run.ManaStatues.IsCommitting || _run.AbandonedCarts.IsCommitting) || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy)) || _run.TryGetConclusion(out _) ||
                _run.GetNodeAdmission(nodeId) == DungeonRunNodeAdmission.PendingChoiceOutstanding)
            { error = "请先完成当前地图事务。"; return false; }
            LevelNode node = _run.CurrentFloor.FindNode(nodeId);
            if (catalog == null || node == null || node.nodeType != LevelNodeType.Interaction ||
                node.completionEffects == null || node.completionEffects.Count != 1 ||
                node.completionEffects[0] == null || (!Handles(node.completionEffects[0].kind) && !IsCrystalCheckpoint(node.completionEffects[0])))
            { error = "节点不是已配置的运行交互事务。"; return false; }
            effect = node.completionEffects[0];
            var errors = new List<string>();
            ValidateEffect(effect, errors);
            if (errors.Count == 0) return true;
            error = string.Join("；", errors);
            return false;
        }

        private DungeonRunInteractionOffer BuildOffer(LevelInteractionEffect effect,
            DungeonRunInteractionCatalog catalog, DungeonRunInteractionDraft draft,
            DungeonRunInteractionSaveData state)
        {
            var offer = new DungeonRunInteractionOffer { Title = effect.summaryText, Phase = DungeonRunInteractionPhase.Ready };
            if (effect.HasDoubleKeyPayment && !_run.DoubleKeyPayments.HasPaidEffect(effect.effectId))
                return Unavailable(offer, "请先支付两张符合本色要求的不同持久非英灵牌。");
            if (draft.Cards.Count == 0 && !_run.HasPlayerState)
                return Unavailable(offer, "请先完成首场战斗，建立本次探索的牌组。");
            switch (effect.kind)
            {
                case LevelInteractionEffectKind.GrantCard:
                    if (!catalog.CanGrant(effect.targetContentId))
                        return Unavailable(offer, "该卡牌尚未完成正式内容验收。");
                    break;
                case LevelInteractionEffectKind.ChooseCard:
                    offer.Phase = DungeonRunInteractionPhase.RewardChoice;
                    var cardOptions = new List<DungeonRunInteractionOption>();
                    foreach (string cardContentId in effect.cardChoices)
                    {
                        CardConfig card = catalog.FindCard(cardContentId);
                        if (card == null || !catalog.CanGrant(cardContentId))
                            return Unavailable(offer, "固定卡牌候选缺失或尚未完成正式内容验收。");
                        cardOptions.Add(new DungeonRunInteractionOption
                        {
                            Id = $"card-choice/{effect.effectId}/{cardContentId}",
                            CardContentId = cardContentId,
                            Title = card.cardName,
                            Description = card.description
                        });
                    }
                    offer.Options = cardOptions.AsReadOnly();
                    break;
                case LevelInteractionEffectKind.CrystalThrowing:
                case LevelInteractionEffectKind.GrantExperience:
                    DungeonRunNaturalLevelRewardRules.RequireConfiguration(catalog,
                        state.effectId == effect.effectId && state.naturalRewardVersion == 1);
                    if (state.effectId != effect.effectId || !state.experienceStarted) break;
                    offer.Title = $"升至 {draft.Progression.CurrentLevel} 级 · 选择奖励";
                    DungeonRunNaturalLevelRewardRules.BuildOffer(catalog, draft, state, offer);
                    break;
                case LevelInteractionEffectKind.RefineCard:
                    offer.Phase = DungeonRunInteractionPhase.EvolutionChoice;
                    if (effect.HasFixedEvolutionContract &&
                        !DungeonRunEvolutionRules.TryValidateFixedContract(
                            catalog, effect.fixedEvolution, out string fixedContractError))
                    {
                        return Unavailable(offer,
                            fixedContractError);
                    }
                    offer.Options = DungeonRunEvolutionRules.GetOptions(
                        draft.Cards,
                        catalog,
                        effect.HasFixedEvolutionContract
                            ? effect.fixedEvolution
                            : null).AsReadOnly();
                    if (offer.Options.Count == 0)
                        return Unavailable(offer, "暂无可进化的卡牌。");
                    break;
                case LevelInteractionEffectKind.ReachLevel:
                    DungeonRunNaturalLevelRewardRules.RequireConfiguration(catalog,
                        state.effectId == effect.effectId && state.naturalRewardVersion == 1);
                    if (state.effectId != effect.effectId || !state.experienceStarted) break;
                    offer.Title = $"达到 Lv{state.targetLevelAfter} · 选择奖励";
                    DungeonRunNaturalLevelRewardRules.BuildOffer(catalog, draft, state, offer);
                    break;
                case LevelInteractionEffectKind.ChooseSeal:
                    BuildSealOffer(effect, catalog, draft, state, offer);
                    break;
            }
            return offer;
        }

        private void BuildSealOffer(LevelInteractionEffect effect, DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData state, DungeonRunInteractionOffer offer)
        {
            if (!_run.CanReceiveSealRewards || !catalog.CanReceiveSeals)
            {
                offer.Phase = DungeonRunInteractionPhase.SealChoice;
                offer.Options = catalog.Wallet != null && effect.cashOutJade > 0
                    ? new[] { new DungeonRunInteractionOption { Id = "cash-out",
                        Title = $"化为灵玉 · +{catalog.Wallet.Preview(effect.cashOutJade).FinalJade}" } }
                    : Array.Empty<DungeonRunInteractionOption>();
                if (offer.Options.Count == 0) Unavailable(offer, "取得玉印后才能钤印。");
                return;
            }
            IReadOnlyList<CardSealSpec> choices = ResolveSealChoices(effect, catalog, state);
            if (UsesRandomSealPool(effect) && choices.Count == 0)
            {
                if (!TryGetRandomSealPoolCandidates(effect, catalog,
                        out _, out string poolError))
                    Unavailable(offer, poolError);
                return;
            }
            CardSealSpec seal = state.effectId == effect.effectId
                ? choices.FirstOrDefault(item => string.Equals(item.contentId,
                    state.selectedSealContentId, StringComparison.OrdinalIgnoreCase)) : null;
            var options = new List<DungeonRunInteractionOption>();
            if (seal == null)
            {
                offer.Phase = DungeonRunInteractionPhase.SealChoice;
                foreach (CardSealSpec candidate in choices)
                    options.Add(new DungeonRunInteractionOption
                    {
                        Id = "seal/" + candidate.contentId, Seal = candidate.Clone(),
                        Title = SealName(candidate.effect), Description = SealDescription(candidate)
                    });
            }
            else
            {
                offer.Phase = DungeonRunInteractionPhase.SealTarget;
                offer.SelectedSeal = seal.Clone();
                foreach (var saved in draft.Cards)
                {
                    CardConfig source = catalog.FindCard(saved.contentId);
                    if (source == null) continue;
                    CardConfig instance = DungeonRunCardCodec.Restore(saved, DungeonRunCardCodec.CreateResolver(catalog.Cards));
                    try
                    {
                        if (!CardSealRules.CanAssign(instance, seal,
                                isEvolutionResultEligible: catalog.CanEvolveTo)) continue;
                        options.Add(new DungeonRunInteractionOption
                        {
                            Id = $"host/{effect.effectId}/{seal.contentId}/{saved.runtimeInstanceId}",
                            CardContentId = saved.contentId, CardRuntimeInstanceId = saved.runtimeInstanceId,
                            Seal = seal.Clone(), Title = source.cardName, Description = SealDescription(seal)
                        });
                    }
                    finally { DestroyInstance(instance); }
                }
                if (options.Count == 0)
                    offer.Message = seal.effect == CardSealEffect.Evolution
                        ? "当前没有可化形的卡牌。"
                        : "没有可钤印的卡牌：需要空印位。";
            }
            // 化玉仍由同一事务结算；宿主选择阶段无需先提交一次返回检查点。
            if (effect.cashOutJade > 0 && catalog.Wallet != null)
                options.Add(new DungeonRunInteractionOption
                {
                    Id = "cash-out",
                    Title = $"变现 · +{catalog.Wallet.Preview(effect.cashOutJade).FinalJade} 灵玉",
                    Description = "放弃本匣全部灵印"
                });
            offer.Options = options.AsReadOnly();
        }

        private bool TryFreezeRandomSealChoices(
            LevelInteractionEffect effect,
            DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionSaveData next,
            out string error)
        {
            if (!TryGetRandomSealPoolCandidates(effect, catalog,
                    out List<CardSealSpec> candidates, out error))
                return false;

            DungeonRunSealRewardPool pool = catalog.FindSealRewardPool(
                effect.sealRewardPoolId);
            var random = new SeededRandomSource(RewardSeed(_run.RunId, effect.effectId, 0));
            int count = Math.Min(effect.amount, candidates.Count);
            var selected = new List<CardSealSpec>(count);
            var preferredIds = new HashSet<string>(
                pool.PreferredCandidateContentIds ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            var preferred = candidates.Where(candidate =>
                preferredIds.Contains(candidate.contentId)).ToList();
            random.Shuffle(preferred);
            int preferredCount = Math.Min(
                Math.Max(0, pool.PreferredCandidateCount),
                Math.Min(count, preferred.Count));
            selected.AddRange(preferred.Take(preferredCount));
            var selectedIds = new HashSet<string>(
                selected.Select(candidate => candidate.contentId),
                StringComparer.OrdinalIgnoreCase);
            var remaining = candidates.Where(candidate =>
                !selectedIds.Contains(candidate.contentId)).ToList();
            random.Shuffle(remaining);
            selected.AddRange(remaining.Take(count - selected.Count));
            next.rewardSealPoolContentId = pool.ContentId;
            next.rewardSealIds = selected.Select(item => item.contentId).ToList();
            return true;
        }

        private static bool TryGetRandomSealPoolCandidates(
            LevelInteractionEffect effect,
            DungeonRunInteractionCatalog catalog,
            out List<CardSealSpec> candidates,
            out string error)
        {
            candidates = new List<CardSealSpec>();
            error = null;
            DungeonRunSealRewardPool pool = catalog.FindSealRewardPool(
                effect.sealRewardPoolId);
            if (pool == null)
            {
                error = "当前账号的灵印奖励池尚未准备好。";
                return false;
            }
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardSealSpec candidate in pool.Candidates ?? Array.Empty<CardSealSpec>())
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.contentId) ||
                    !Enum.IsDefined(typeof(CardSealEffect), candidate.effect) || candidate.value <= 0 ||
                    !ids.Add(candidate.contentId))
                {
                    error = "当前账号的灵印奖励池包含无效或重复候选。";
                    return false;
                }
                candidates.Add(candidate);
            }
            if (candidates.Count == 0)
            {
                error = "当前账号尚未解锁可进入普通钤印匣的灵印。";
                return false;
            }
            if (pool.PreferredCandidateCount < 0)
            {
                error = "灵印奖励池的优先候选数量不能为负数。";
                return false;
            }
            var preferredIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string preferredId in
                     pool.PreferredCandidateContentIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(preferredId) ||
                    !preferredIds.Add(preferredId) ||
                    !ids.Contains(preferredId))
                {
                    error = "灵印奖励池包含空、重复或不属于完整池的优先候选。";
                    return false;
                }
            }
            return true;
        }

        private static IReadOnlyList<CardSealSpec> ResolveSealChoices(
            LevelInteractionEffect effect,
            DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionSaveData state)
        {
            if (!UsesRandomSealPool(effect))
                return effect.sealChoices ?? (IReadOnlyList<CardSealSpec>)Array.Empty<CardSealSpec>();
            if (state.effectId != effect.effectId || state.rewardSealIds == null ||
                state.rewardSealIds.Count == 0)
                return Array.Empty<CardSealSpec>();
            DungeonRunSealRewardPool pool = catalog.FindSealRewardPool(
                state.rewardSealPoolContentId);
            if (pool == null)
                throw new InvalidOperationException("已冻结灵印候选的版本化奖励池缺失。");
            var byId = (pool.Candidates ?? Array.Empty<CardSealSpec>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.contentId))
                .ToDictionary(item => item.contentId, StringComparer.OrdinalIgnoreCase);
            var result = new List<CardSealSpec>(state.rewardSealIds.Count);
            foreach (string id in state.rewardSealIds)
            {
                if (!byId.TryGetValue(id, out CardSealSpec candidate))
                    throw new InvalidOperationException("已冻结灵印候选不再属于对应的账号奖励池。");
                result.Add(candidate);
            }
            return result.AsReadOnly();
        }

        private static bool UsesRandomSealPool(LevelInteractionEffect effect)
            => effect != null && effect.kind == LevelInteractionEffectKind.ChooseSeal &&
               !string.IsNullOrWhiteSpace(effect.sealRewardPoolId);

        private bool AdvanceExperience(string nodeId, LevelInteractionEffect effect,
            DungeonRunInteractionOption selected, DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData next, out string error)
        {
            error = null;
            if (effect.kind == LevelInteractionEffectKind.CrystalThrowing && (!next.experienceStarted || next.experienceOrigin != 1))
                throw new InvalidOperationException("Crystal XP must originate from its durable hit owner.");
            if (!next.experienceStarted)
            {
                next.nodeContentId = nodeId;
                next.effectId = effect.effectId;
                next.experienceStarted = true;
                next.experienceBefore = draft.Progression.CurrentExperience;
                next.settledExperience = draft.Progression.SettleExperience(effect.amount,
                    ExperienceSource.DirectCard, catalog.GrowthPercent, 0);
                draft.ExperienceCollections.Record(_run.CurrentFloorContentId,
                    new ExperienceCollectionTotals(next.settledExperience, 0));
                draft.Progression.Store(draft.Progression.CurrentLevel,
                    checked(draft.Progression.CurrentExperience + next.settledExperience));
            }
            else
            {
                ApplyLevelReward(selected, catalog, draft, next);
            }
            int required = draft.Progression.GetRequiredExperience(draft.Progression.CurrentLevel);
            if (draft.Progression.CurrentExperience < required) return true;
            draft.Progression.Store(checked(draft.Progression.CurrentLevel + 1), draft.Progression.CurrentExperience - required);
            next.rewardIndex++;
            return BuildRewardChoices(
                effect,
                catalog,
                draft,
                next,
                draft.Progression.CurrentLevel,
                false,
                out error);
        }

        /// <summary>
        /// 不存在原始经验数值证据的节点只提交可观察的等级结果。它不推测残余
        /// 经验，也不改动成长定点余量；若真的跨过等级，只提供该目标等级的一次
        /// 正常升级选择。参考装载外的更高等级 Run 不会被降级或重发选择。
        /// </summary>
        private bool AdvanceToTargetLevel(string nodeId, LevelInteractionEffect effect,
            DungeonRunInteractionOption selected, DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData next, out string error)
        {
            error = null;
            if (!next.experienceStarted)
            {
                next.nodeContentId = nodeId;
                next.effectId = effect.effectId;
                next.experienceStarted = true;
                next.targetLevelBefore = draft.Progression.CurrentLevel;
                next.targetLevelAfter = DungeonRunTargetLevelRules.ResolveReachedLevel(
                    draft.Progression, effect.targetLevelResult);
                if (next.targetLevelAfter <= next.targetLevelBefore)
                    return true;

                draft.Progression.Store(next.targetLevelAfter,
                    draft.Progression.CurrentExperience);
                next.rewardIndex++;
                return BuildRewardChoices(
                    effect,
                    catalog,
                    draft,
                    next,
                    next.targetLevelAfter,
                    true,
                    out error);
            }

            ApplyLevelReward(selected, catalog, draft, next);
            return true;
        }

        private bool BuildRewardChoices(LevelInteractionEffect effect,
            DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionDraft draft,
            DungeonRunInteractionSaveData next,
            int reachedLevel,
            bool isReferenceTargetLevel,
            out string error)
        {
            error = null;
            DungeonRunNaturalLevelRewardRules.Freeze(catalog, draft, next,
                new DungeonRunLevelRewardOfferRequest(effect.effectId, reachedLevel,
                    next.rewardIndex, isReferenceTargetLevel),
                new SeededRandomSource(RewardSeed(_run.RunId, effect.effectId, next.rewardIndex)));
            return true;
        }

        private void ApplyLevelReward(DungeonRunInteractionOption selected,
            DungeonRunInteractionCatalog catalog, DungeonRunInteractionDraft draft,
            DungeonRunInteractionSaveData next)
        {
            DungeonRunCardSaveData replacement = DungeonRunNaturalLevelRewardRules.PrepareReward(
                catalog, draft, next, selected);
            if (replacement == null) AddCard(draft, selected.CardContentId, catalog);
            else
            {
                int index = draft.Cards.FindIndex(card => card.runtimeInstanceId == replacement.runtimeInstanceId);
                draft.Cards[index] = replacement;
                draft.ForcedOpeningCardId = replacement.runtimeInstanceId;
            }
            next.committedChoiceIds.Add(selected.Id);
            DungeonRunNaturalLevelRewardRules.Clear(next);
        }

        private int AddCard(DungeonRunInteractionDraft draft, string contentId, DungeonRunInteractionCatalog catalog)
        {
            if (!catalog.CanGrant(contentId)) throw new InvalidOperationException("发卡目标未完成验收或不存在。");
            int id = _run.AllocateInteractionCardId();
            while (draft.Cards.Exists(card => card.runtimeInstanceId == id)) id = _run.AllocateInteractionCardId();
            CardConfig definition = catalog.FindCard(contentId);
            draft.Cards.Add(DungeonRunCardCodec.CaptureNew(definition, id,
                DungeonRunCardCodec.CreateResolver(catalog.Cards)));
            return id;
        }

        private bool CommitCheckpoint(LevelInteractionEffect effect, DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData next, out DungeonRunInteractionOffer offer,
            Action persist)
        {
            offer = BuildOffer(effect, catalog, draft, next);
            if (offer.Phase == DungeonRunInteractionPhase.Unavailable)
                throw new InvalidOperationException(offer.Message);
            var previous = _state;
            try
            {
                if (effect.kind == LevelInteractionEffectKind.CrystalThrowing)
                    CommitCrystalChoiceDraft(draft, next, persist);
                else _run.CommitInteractionDraft(draft, () => { _state = next; persist?.Invoke(); });
            }
            catch { _state = previous; throw; }
            return true;
        }

        private bool CommitTerminal(string nodeId, LevelInteractionEffect effect, string choiceId,
            DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData next,
            int before, int after, int amount, int targetId, string targetContentId,
            out DungeonRunInteractionOffer offer, out string error, Action persist)
        {
            offer = null;
            error = null;
            var previous = _state;
            LevelRunSession floor = _run.CurrentFloor;
            LevelRunSession previousFloor = floor.CaptureTransactionState();
            LevelInteractionReceipt receipt = default;
            try
            {
                _run.CommitInteractionDraft(draft, () =>
                {
                    if (!floor.TryRecordExternalEffect(nodeId, effect, before, after, amount,
                        out receipt, out string receiptError, targetId, targetContentId))
                        throw new InvalidOperationException(receiptError);
                    next.completed.Add(new DungeonRunInteractionDecisionSaveData
                        { effectId = effect.effectId, choiceId = choiceId });
                    _state = new DungeonRunInteractionSaveData { completed = next.completed };
                    persist?.Invoke();
                });
            }
            catch
            {
                floor.AdoptPreparedState(previousFloor);
                _state = previous;
                throw;
            }
            offer = Completed(effect, receipt);
            return true;
        }

        private bool CommitCashOut(string nodeId, LevelInteractionEffect effect, string choiceId,
            DungeonRunInteractionCatalog catalog, DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData next,
            out DungeonRunInteractionOffer offer, out string error, Action persist)
        {
            offer = null;
            error = null;
            IDungeonRunInteractionWallet wallet = catalog.Wallet;
            if (wallet == null) { error = "缺少地图灵玉账本。"; return false; }
            int balance = wallet.Balance;
            var run = _run.RunProgression;
            RunProgressionSnapshot counters = run.Capture();
            bool succeeded = false;
            try
            {
                JadeGainResult expected = wallet.Preview(effect.cashOutJade);
                JadeGainResult actual = wallet.Credit(effect.cashOutJade);
                if (actual.BaseJade != effect.cashOutJade || actual.FinalJade != expected.FinalJade ||
                    actual.FinalJade < 0 || (long)wallet.Balance - balance != actual.FinalJade)
                    throw new InvalidOperationException("化玉账本没有提交配置对应的精确金额。");
                succeeded = CommitTerminal(nodeId, effect, choiceId, draft, next, balance, wallet.Balance,
                    actual.FinalJade, 0, string.Empty, out offer, out error, persist);
                return succeeded;
            }
            catch (Exception exception) { error = "化玉失败：" + exception.Message; return false; }
            finally
            {
                if (!succeeded)
                {
                    wallet.RestoreBalance(balance);
                    run.Restore(counters);
                }
            }
        }

        private void ValidateRestoredState()
        {
            _state.completed ??= new List<DungeonRunInteractionDecisionSaveData>();
            _state.committedChoiceIds ??= new List<string>();
            _state.rewardCardIds ??= new List<string>();
            _state.naturalRewardCandidates ??= new List<DungeonRunNaturalRewardCandidateSaveData>();
            _state.rewardSealIds ??= new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in _state.completed)
                if (item == null || string.IsNullOrWhiteSpace(item.effectId) || !ids.Add(item.effectId))
                    throw new InvalidOperationException("地图交互存档包含无效或重复的终态选择。");
            if (!HasPendingChoice) return;
            LevelNode node = _run.CurrentFloor.FindNode(_state.nodeContentId);
            LevelInteractionEffect effect = node?.completionEffects?.Find(item => item?.effectId == _state.effectId);
            if (effect == null || (!Handles(effect.kind) && !IsCrystalCheckpoint(effect)) || _state.rewardIndex < 0 || _state.settledExperience < 0 ||
                _run.CurrentFloor.TryGetInteractionReceipt(_state.effectId, out _))
                throw new InvalidOperationException("地图交互检查点与当前楼层配置不一致。");
            LevelNodeViewState progress = _run.CurrentFloor.GetNodeState(_state.nodeContentId);
            // 关闭面板会把 Completing 恢复为 Available，但不能丢掉已发经验或
            // 已达成目标等级、仍待选卡的检查点。
            if ((progress != LevelNodeViewState.Available && progress != LevelNodeViewState.Completing) ||
                (effect.kind != LevelInteractionEffectKind.ChooseSeal && !_state.experienceStarted))
                throw new InvalidOperationException("待选地图奖励不再属于可继续的交互节点。");
            bool isLevelProgressionEffect = effect.kind == LevelInteractionEffectKind.GrantExperience ||
                effect.kind == LevelInteractionEffectKind.ReachLevel || effect.kind == LevelInteractionEffectKind.CrystalThrowing;
            if (_state.experienceStarted && (!isLevelProgressionEffect ||
                _state.rewardIndex <= 0 || !DungeonRunNaturalLevelRewardRules.HasChoices(_state)))
                throw new InvalidOperationException("地图等级进度存档缺少合法奖励检查点。");
            if (isLevelProgressionEffect) DungeonRunNaturalLevelRewardRules.ValidateCheckpoint(_state);
            if (effect.kind == LevelInteractionEffectKind.ReachLevel &&
                (_state.targetLevelBefore < 1 ||
                 _state.targetLevelAfter < _state.targetLevelBefore ||
                 _state.targetLevelAfter != effect.targetLevelResult.targetLevel))
            {
                throw new InvalidOperationException(
                    "地图目标等级存档与参考结果合同不一致。");
            }
            if (effect.kind == LevelInteractionEffectKind.ChooseSeal && UsesRandomSealPool(effect))
            {
                if (!string.Equals(_state.rewardSealPoolContentId,
                        effect.sealRewardPoolId, StringComparison.OrdinalIgnoreCase) ||
                    _state.rewardSealIds.Count == 0 ||
                    _state.rewardSealIds.Count > effect.amount ||
                    _state.rewardSealIds.Any(string.IsNullOrWhiteSpace) ||
                    _state.rewardSealIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                    _state.rewardSealIds.Count)
                    throw new InvalidOperationException("地图随机选印存档缺少合法的冻结候选。");
                if (!string.IsNullOrEmpty(_state.selectedSealContentId) &&
                    !_state.rewardSealIds.Contains(_state.selectedSealContentId,
                        StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("地图选印存档引用了冻结候选之外的灵印。");
            }
            else if (!isLevelProgressionEffect && !string.IsNullOrEmpty(_state.selectedSealContentId) &&
                (effect.kind != LevelInteractionEffectKind.ChooseSeal ||
                 !effect.sealChoices.Exists(seal => seal.contentId == _state.selectedSealContentId)))
                throw new InvalidOperationException("地图选印存档引用了候选之外的灵印。");
        }

        private static DungeonRunInteractionSaveData Clone(DungeonRunInteractionSaveData source)
            => JsonUtility.FromJson<DungeonRunInteractionSaveData>(JsonUtility.ToJson(source));

        private static DungeonRunInteractionOffer Completed(LevelInteractionEffect effect, LevelInteractionReceipt receipt)
            => new DungeonRunInteractionOffer { Phase = DungeonRunInteractionPhase.Completed, Title = effect.summaryText, Receipt = receipt };

        private static DungeonRunInteractionOffer Unavailable(DungeonRunInteractionOffer offer, string reason)
        { offer.Phase = DungeonRunInteractionPhase.Unavailable; offer.Message = reason; return offer; }

        private static int RewardSeed(string runId, string effectId, int index)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char value in runId + "/" + effectId + "/" + index) hash = (hash ^ value) * 16777619;
                return (int)(hash & int.MaxValue);
            }
        }

        private static string SealName(CardSealEffect effect) => effect switch
        { CardSealEffect.DoubleDamage => "双伤印", CardSealEffect.Draw => "引卷印",
            CardSealEffect.Armor => "罡气印", CardSealEffect.Amount => "数量印",
            CardSealEffect.BengLie => "裂空印", CardSealEffect.Evolution => "化形印",
            CardSealEffect.Refund => "返灵印", CardSealEffect.ManaRebate => "灵涌印", _ => "灵印" };

        private static string SealDescription(CardSealSpec seal) => seal.effect switch
        {
            CardSealEffect.DoubleDamage => "伤害翻倍",
            CardSealEffect.Draw => $"抽{Math.Max(1, seal.value)}张牌",
            CardSealEffect.Armor => $"护甲+{seal.value}",
            CardSealEffect.Amount => $"数量+{seal.value}",
            CardSealEffect.BengLie => $"裂空+{seal.value}%",
            CardSealEffect.Evolution => "将一张卡牌进化为更强形态，占用1个印位，保留原有灵印。",
            CardSealEffect.Refund => $"出牌结算后返还实际支付灵力×{seal.value}；零支付不触发。",
            CardSealEffect.ManaRebate => $"实际支付耗尽全部可用灵力时，结算后回涌支付量×{seal.value}；以支付时点判定，零支付不触发。",
            _ => "钤入一张有空印位的合法卡牌。"
        };

        private static void DestroyInstance(CardConfig instance)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
            else UnityEngine.Object.DestroyImmediate(instance);
        }
    }
}
