using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 卡牌归元的耐久提交边界：拥有献牌/化玉、回执、互斥和钱包恢复。
    /// 保存成功才发布结果；失败恢复同一楼层、完整牌组/关联、属性和经济参与者。
    /// 旧调用可省略持久化委托，生产组装必须注入严格保存。
    /// Scale review: direct Mana exchange and recovery of its historical paid
    /// rewards remain the same conversion commit boundary. This owner alone
    /// mutates its legacy journal; no presentation or floor ownership is added.
    /// Revisit the boundary before adding another independent reward lifecycle.
    /// </summary>
    public sealed class RunCardConversionTransaction
    {
        private readonly DungeonRunSession _run;
        private bool _committing;
        private IDungeonRunInteractionWallet _unrestoredWallet;
        private int _unrestoredBalance;
        private RunManaRepairPickupJournal _manaPickups;

        internal RunCardConversionTransaction(DungeonRunSession run, RunManaRepairPickupSaveData manaPickups = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _manaPickups = new RunManaRepairPickupJournal(manaPickups);
        }

        internal bool IsCommitting => _committing || _unrestoredWallet != null;

        public bool TryConvert(string nodeContentId, LevelInteractionEffect effect,
            int cardInstanceId, RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            out LevelInteractionReceipt receipt, out string error, Action persist = null)
        {
            receipt = default;
            if (!TryBegin(out error)) return false;
            try
            {
                if (!RunCardConversionChoices.TryInspectForCommit(this, _run, nodeContentId, effect, profile,
                        cardPool, out RunCardConversionOffer offer, out error)) return false;
                if (offer.Completed)
                    return _run.ApplyCardConversion(nodeContentId, effect, cardInstanceId,
                        profile, cardPool, out receipt, out error);
                LevelInteractionReceipt resolved = default;
                _run.CommitRewardState(() =>
                {
                    if (!_run.ApplyCardConversion(nodeContentId, effect, cardInstanceId,
                            profile, cardPool, out resolved, out string failure) || !resolved.NewlyApplied)
                        throw new InvalidOperationException(failure ?? "Conversion state changed during commit.");
                    // A Mana exchange is one decision and one durable result.
                    // Card removal, modifier and node completion are saved
                    // together; only historical saves can own deferred pickups.
                    if (profile.RequiresManaPickup &&
                        !_run.CurrentFloor.TryCompleteInteraction(nodeContentId, out string completionError))
                        throw new InvalidOperationException(completionError);
                }, persist);
                receipt = resolved;
                return true;
            }
            catch (Exception exception)
            { error = "Conversion was not committed; Run restored: " + exception.Message; return false; }
            finally { _committing = false; }
        }

        public bool HasPendingManaPickup(string floorContentId, string nodeContentId)
            => _manaPickups.HasPending(floorContentId, nodeContentId);

        /// <summary>
        /// Historical schema-41 rewards still owed on this floor. The map may
        /// settle these with TryCollectManaPickup without another player click.
        /// This query never grants or persists anything and does not assume an
        /// ActiveInteractionNodeId survived recovery.
        /// </summary>
        public IReadOnlyList<string> GetPendingManaPickupNodeIds()
            => _manaPickups.PendingNodeIds(_run.CurrentFloorContentId);

        /// <summary>All still-owed schema-41 rewards, including completed floors.</summary>
        public IReadOnlyList<RunLegacyManaPickup> GetPendingLegacyManaPickups()
            => _manaPickups.PendingPickups();

        /// <summary>
        /// Settles an already-paid legacy reward without reopening its floor or
        /// removing another card. Modifier and journal proof share one strict
        /// save. Current-floor recovery retains the existing pickup contract.
        /// </summary>
        public bool TryCollectLegacyManaPickup(string floorContentId, string nodeContentId,
            out LevelInteractionReceipt receipt, out string error, Action persist)
        {
            if (floorContentId == _run.CurrentFloorContentId)
                return TryCollectManaPickup(nodeContentId, out receipt, out error, persist);
            receipt = default;
            if (!TryBegin(out error)) return false;
            RunManaRepairPickupJournal previous = _manaPickups;
            try
            {
                var floor = _run.CurrentFloor;
                var entry = _manaPickups.Find(floorContentId, nodeContentId);
                if (entry == null || _run.TryGetConclusion(out _) || floor.ActiveEncounter.HasValue ||
                    floor.TryGetPendingReturn(out _) || !string.IsNullOrEmpty(floor.ActiveInteractionNodeId) ||
                    !_run.TryReadCompletedInteractionReceipt(floorContentId, nodeContentId, entry.effectId, out var repair) ||
                    !RunManaRepairPickupJournal.MatchesRepair(repair, entry))
                { error = "Legacy Mana recovery requires a proven completed platform and an idle map."; return false; }
                if (entry.collected)
                { receipt = RunManaRepairPickupJournal.PickupReceipt(entry, false); return true; }
                if (persist == null) throw new InvalidOperationException("Legacy Mana recovery requires strict atomic persistence.");
                _run.CommitRewardState(() =>
                {
                    int before = _run.BaseManaBonus;
                    _run.ApplyRepairedManaPickup(entry.amount);
                    _manaPickups = _manaPickups.WithCollection(floorContentId, nodeContentId, before, _run.BaseManaBonus);
                }, persist);
                receipt = RunManaRepairPickupJournal.PickupReceipt(_manaPickups.Find(floorContentId, nodeContentId), true);
                return true;
            }
            catch (Exception exception)
            { _manaPickups = previous; error = "Legacy Mana recovery was not committed; Run restored: " + exception.Message; return false; }
            finally { _committing = false; }
        }

        public bool TryInspectManaPickup(string nodeContentId, out RunManaRepairPickupOffer offer, out string error)
        {
            offer = null;
            if (_run.IsRewardCommitInProgress || HasOtherRewardBoundary)
            { error = "Complete the current Run reward transaction first."; return false; }
            if (!TryRequireManaPickup(nodeContentId, out var entry, out error)) return false;
            offer = new RunManaRepairPickupOffer(entry); return true;
        }

        public bool TryCollectManaPickup(string nodeContentId, out LevelInteractionReceipt receipt,
            out string error, Action persist)
        {
            receipt = default;
            if (!TryBegin(out error)) return false;
            RunManaRepairPickupJournal previous = _manaPickups;
            try
            {
                if (!TryRequireManaPickup(nodeContentId, out var entry, out error)) return false;
                if (entry.collected)
                { receipt = RunManaRepairPickupJournal.PickupReceipt(entry, false); return true; }
                if (persist == null) throw new InvalidOperationException("Mana pickup requires strict atomic persistence.");
                _run.CommitRewardState(() =>
                {
                    // A repair save precedes its presentation and node-finalization.
                    // Reload/recovery may therefore leave this already-paid node
                    // Completing or Available. Finish that exact receipt before
                    // collecting, within the same rollback/save boundary.
                    var floor = _run.CurrentFloor;
                    if (floor.GetNodeState(nodeContentId) != LevelNodeViewState.Completed)
                    {
                        if (floor.ActiveInteractionNodeId != nodeContentId &&
                            !floor.TryBeginInteraction(nodeContentId, out string beginError))
                            throw new InvalidOperationException(beginError);
                        if (!floor.TryCompleteInteraction(nodeContentId, out string completeError))
                            throw new InvalidOperationException(completeError);
                    }
                    int before = _run.BaseManaBonus;
                    _run.ApplyRepairedManaPickup(entry.amount);
                    _manaPickups = _manaPickups.WithCollection(_run.CurrentFloorContentId, nodeContentId,
                        before, _run.BaseManaBonus);
                }, persist);
                receipt = RunManaRepairPickupJournal.PickupReceipt(
                    _manaPickups.Find(_run.CurrentFloorContentId, nodeContentId), true);
                return true;
            }
            catch (Exception exception)
            { _manaPickups = previous; error = "Mana pickup was not committed; Run restored: " + exception.Message; return false; }
            finally { _committing = false; }
        }

        private bool TryRequireManaPickup(string nodeId, out RunManaRepairPickupEntry entry, out string error)
        {
            error = null; entry = _manaPickups.Find(_run.CurrentFloorContentId, nodeId);
            var floor = _run.CurrentFloor;
            LevelNodeViewState state = floor.GetNodeState(nodeId);
            bool ownInteraction = floor.ActiveInteractionNodeId == nodeId;
            bool legalState = state == LevelNodeViewState.Completed || state == LevelNodeViewState.Available ||
                (state == LevelNodeViewState.Completing && ownInteraction);
            if (entry == null || _run.TryGetConclusion(out _) || floor.ActiveEncounter.HasValue ||
                floor.TryGetPendingReturn(out _) ||
                (!string.IsNullOrEmpty(floor.ActiveInteractionNodeId) && !ownInteraction) || !legalState ||
                !floor.TryGetInteractionReceipt(entry.effectId, out var repair) ||
                !RunManaRepairPickupJournal.MatchesRepair(repair, entry))
            { error = "Mana pickup requires its repaired platform on an idle map."; return false; }
            return true;
        }

        internal RunManaRepairPickupSaveData CaptureManaPickupSaveData() => _manaPickups.Capture();

        public bool TryCashOut(string nodeContentId, LevelInteractionEffect effect,
            RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            IDungeonRunInteractionWallet wallet, out LevelInteractionReceipt receipt, out string error,
            Action persist = null)
        {
            receipt = default;
            if (!TryBegin(out error)) return false;
            int balance = 0;
            bool walletTouched = false, succeeded = false;
            try
            {
                if (!RunCardConversionChoices.TryInspectForCommit(this, _run, nodeContentId, effect, profile,
                        cardPool, out RunCardConversionOffer offer, out error)) return false;
                if (!profile.AllowsCashOut)
                { error = "This conversion does not allow cash-out."; return false; }
                if (offer.Completed)
                {
                    if (offer.Receipt.Kind != LevelInteractionEffectKind.ConvertCard ||
                        offer.Receipt.TargetRuntimeInstanceId != 0 ||
                        !string.Equals(offer.Receipt.TargetContentId, profile.contentId, StringComparison.OrdinalIgnoreCase))
                    { error = "Conversion was already committed with a different disposition."; return false; }
                    receipt = offer.Receipt;
                    succeeded = true;
                    return true;
                }
                LevelRunSession floor = _run.CurrentFloor;
                if (wallet == null || floor.ActiveInteractionNodeId != nodeContentId)
                { error = "Cash-out requires a wallet and the active interaction."; return false; }
                balance = wallet.Balance;
                if (balance < 0) throw new InvalidOperationException("Cash-out wallet balance is invalid.");
                LevelInteractionReceipt resolved = default;
                _run.CommitRewardState(() =>
                {
                    RequireUncommittedInteraction(floor, nodeContentId, effect);
                    int runJadeBefore = _run.RunProgression.RunJadeEarned;
                    walletTouched = true;
                    JadeGainResult expected = wallet.Preview(effect.amount);
                    RequireUncommittedInteraction(floor, nodeContentId, effect);
                    if (expected.Source != JadeSource.CashOut || expected.BaseJade != effect.amount ||
                        expected.FinalJade < 0 || !expected.AffectedByGreed)
                        throw new InvalidOperationException("Cash-out preview returned an invalid receipt.");
                    JadeGainResult actual = wallet.Credit(effect.amount);
                    RequireUncommittedInteraction(floor, nodeContentId, effect);
                    int after = wallet.Balance;
                    if (actual.Source != JadeSource.CashOut || actual.BaseJade != effect.amount ||
                        actual.FinalJade != expected.FinalJade || actual.FinalJade < 0 ||
                        actual.AffectedByGreed != expected.AffectedByGreed ||
                        (long)after - balance != actual.FinalJade ||
                        (long)_run.RunProgression.RunJadeEarned - runJadeBefore != actual.FinalJade)
                        throw new InvalidOperationException("Cash-out ledger returned an inconsistent receipt.");
                    if (!floor.TryRecordExternalEffect(nodeContentId, effect, balance, after,
                            actual.FinalJade, out resolved, out string failure) || !resolved.NewlyApplied)
                        throw new InvalidOperationException(failure ?? "Cash-out state changed during commit.");
                }, persist);
                receipt = resolved;
                succeeded = true;
                return true;
            }
            catch (Exception exception)
            { error = "Cash-out was not committed; Run restored: " + exception.Message; return false; }
            finally
            {
                if (!succeeded && walletTouched && !TryRestoreWallet(wallet, balance, out string recoveryError))
                {
                    _unrestoredWallet = wallet;
                    _unrestoredBalance = balance;
                    error += " Wallet recovery is incomplete; later commits are blocked: " + recoveryError;
                }
                _committing = false;
            }
        }

        // Only this owner can inspect an offer while holding its own commit lock.
        // External/public inspection remains subject to the aggregate Run gate.
        internal bool CanInspectOwnCommit(DungeonRunSession run)
            => ReferenceEquals(_run, run) && _committing && _unrestoredWallet == null &&
                !HasOtherRewardBoundary;

        private bool HasOtherRewardBoundary => _run.Interactions.IsCommitting || _run.GemChests.IsCommitting ||
            _run.DoubleKeyPayments.IsCommitting || _run.VictorySealRewards.IsCommitting ||
            _run.FloorExits.IsCommitting || _run.Arcanas.IsCommitting ||
            _run.ChickenOfferings.IsCommitting || _run.ManaStatues.IsCommitting ||
            _run.AbandonedCarts.IsCommitting || _run.SideEvents.IsCommitting || _run.Crystals.IsCommitting || _run.Crystals.HasPendingEvent || _run.Interactions.HasPendingChoice ||
            _run.Arcanas.HasPendingChoice || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy);

        private bool TryBegin(out string error)
        {
            error = null;
            if (_committing || HasOtherRewardBoundary)
            { error = "Complete the current Run reward transaction first."; return false; }
            _committing = true;
            if (_unrestoredWallet != null)
            {
                if (!TryRestoreWallet(_unrestoredWallet, _unrestoredBalance, out string recoveryError))
                {
                    _committing = false;
                    error = "The prior conversion wallet is still unrecovered: " + recoveryError;
                    return false;
                }
                _unrestoredWallet = null;
            }
            return true;
        }

        private void RequireUncommittedInteraction(LevelRunSession expected, string nodeId, LevelInteractionEffect effect)
        {
            if (!ReferenceEquals(_run.CurrentFloor, expected) || expected.ActiveInteractionNodeId != nodeId ||
                expected.GetNodeState(nodeId) != LevelNodeViewState.Completing ||
                expected.TryGetInteractionReceipt(effect.effectId, out _) ||
                !RunCardConversionChoices.TryValidateNodeEffect(expected, nodeId, effect, out _))
                throw new InvalidOperationException("Conversion interaction changed during the wallet callback.");
        }

        private static bool TryRestoreWallet(IDungeonRunInteractionWallet wallet, int balance, out string error)
        {
            error = null;
            try { wallet.RestoreBalance(balance); }
            catch (Exception exception) { error = exception.Message; }
            try
            {
                if (wallet.Balance == balance) return true;
                error = (error == null ? string.Empty : error + "; ") + "Wallet balance was not restored.";
            }
            catch (Exception exception) { error = (error ?? string.Empty) + "; " + exception.Message; }
            return false;
        }

        internal static bool TryCommit(
            List<DungeonRunCardSaveData> cards,
            DungeonRunSharedState shared,
            LevelRunSession floor,
            string nodeContentId,
            LevelInteractionEffect effect,
            int cardInstanceId,
            RunCardConversionRuleProfile profile,
            IReadOnlyList<CardConfig> cardPool,
            out LevelInteractionReceipt receipt,
            out string error)
        {
            receipt = default;
            error = null;
            if (cards == null || shared == null || floor == null)
            {
                error = "归元事务缺少 Run 状态。";
                return false;
            }
            if (!RunCardConversionChoices.TryValidateNodeEffect(
                    floor, nodeContentId, effect, out error) ||
                !RunCardConversionChoices.TryValidateProfile(
                    effect, profile, cardPool, out error))
                return false;
            if (cardInstanceId <= 0)
            {
                error = "归元目标必须是有效的卡牌实例 ID。";
                return false;
            }

            if (floor.TryGetInteractionReceipt(effect.effectId, out receipt))
            {
                if (receipt.Kind != LevelInteractionEffectKind.ConvertCard ||
                    !string.Equals(receipt.TargetContentId, profile.contentId,
                        StringComparison.OrdinalIgnoreCase) ||
                    receipt.TargetRuntimeInstanceId != cardInstanceId)
                {
                    error = $"交互效果 {effect.effectId} 已归元卡牌实例 " +
                            $"{receipt.TargetRuntimeInstanceId}，不能改绑为 " +
                            $"{cardInstanceId}。";
                    return false;
                }
                return true;
            }
            int cardIndex = cards.FindIndex(card =>
                card != null && card.runtimeInstanceId == cardInstanceId);
            if (cardIndex < 0)
            {
                error = $"Run 牌组中找不到卡牌实例：{cardInstanceId}。";
                return false;
            }
            DungeonRunCardSaveData saved = cards[cardIndex];
            if (!TryResolveSavedRule(saved, profile, cardPool,
                    out RunCardConversionRule rule, out error))
                return false;

            int valueBefore = GetValue(shared, rule.outcome);
            int effectiveAmount = rule.GetEffectiveAmount(valueBefore);
            if (effectiveAmount <= 0)
            {
                error = "归元属性已达上限，不消耗卡牌。";
                return false;
            }
            cards.RemoveAt(cardIndex);
            bool committed = false;
            try
            {
                int appliedAmount = Apply(shared, rule.outcome, effectiveAmount);
                if (appliedAmount <= 0)
                {
                    error = "归元没有产生有效的 Run 修正。";
                    return false;
                }
                committed = floor.TryRecordExternalEffect(
                    nodeContentId, effect, valueBefore,
                    GetValue(shared, rule.outcome), appliedAmount,
                    out receipt, out error,
                    targetRuntimeInstanceId: cardInstanceId);
                return committed;
            }
            catch (Exception exception)
            {
                receipt = default;
                error = "归元事务失败：" + exception.Message;
                return false;
            }
            finally
            {
                if (!committed)
                {
                    if (rule.outcome == RunCardConversionOutcome.Luck)
                    {
                        // AddLuck deliberately rejects negative awards. Use
                        // the Run owner's restore contract for atomic rollback.
                        RunProgressionState progression = shared.RunProgression;
                        progression.Restore(new RunProgressionSnapshot(valueBefore,
                            progression.RunGreedBonus, progression.RunJadeEarned,
                            progression.RunGrowthBonus));
                    }
                    else
                        Apply(shared, rule.outcome,
                            valueBefore - GetValue(shared, rule.outcome));
                    // Reinsert the same saved object, including seals and
                    // limit breaks, at exactly its previous position.
                    cards.Insert(cardIndex, saved);
                }
            }
        }

        private static bool TryResolveSavedRule(DungeonRunCardSaveData saved,
            RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            out RunCardConversionRule rule, out string error)
        {
            rule = null;
            error = null;
            CardConfig instance = null;
            try
            {
                instance = DungeonRunCardCodec.Restore(saved, DungeonRunCardCodec.CreateResolver(cardPool));
                if (profile.TryResolveRule(instance, out rule)) return true;
                error = $"卡牌实例 {saved.runtimeInstanceId} 不符合归元支付规则。";
                return false;
            }
            catch (Exception exception)
            {
                error = "归元目标无法恢复：" + exception.Message;
                return false;
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static int GetValue(
            DungeonRunSharedState shared,
            RunCardConversionOutcome outcome)
        {
            return outcome switch
            {
                RunCardConversionOutcome.BaseArmorPerTurn =>
                    shared.BaseArmorPerTurnBonus,
                RunCardConversionOutcome.Might => shared.MightBonus,
                RunCardConversionOutcome.BaseMana => shared.BaseManaBonus,
                RunCardConversionOutcome.Area => shared.AreaBonus,
                RunCardConversionOutcome.Luck => shared.RunProgression.RunLuckBonus,
                _ => 0
            };
        }

        private static int Apply(
            DungeonRunSharedState shared,
            RunCardConversionOutcome outcome,
            int amount)
        {
            return outcome switch
            {
                RunCardConversionOutcome.BaseArmorPerTurn =>
                    shared.AddBaseArmorPerTurn(amount),
                RunCardConversionOutcome.Might => shared.AddMight(amount),
                RunCardConversionOutcome.BaseMana => shared.AddBaseMana(amount),
                RunCardConversionOutcome.Area => shared.AddArea(amount),
                RunCardConversionOutcome.Luck => shared.RunProgression.AddLuck(amount),
                _ => 0
            };
        }
    }
}
