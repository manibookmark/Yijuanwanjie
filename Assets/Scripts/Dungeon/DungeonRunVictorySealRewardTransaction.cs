using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 胜利灵印事务向楼层会话提交终态的最小接口。卡牌写入留在 Dungeon Run，
    /// 节点完成与奖励步骤留在 Level Run，二者不互相泄漏可变集合。
    /// </summary>
    internal interface ILevelVictorySealRewardCommitTarget
    {
        bool TryGetPendingVictorySealReward(
            out LevelVictorySealRewardOffer offer);

        bool TryGetVictorySealRewardReceipt(
            string rewardContentId,
            out LevelVictorySealRewardReceipt receipt);

        bool TryCommitVictorySealAssignment(
            string rewardContentId,
            int cardRuntimeInstanceId,
            int slotIndex,
            out LevelVictorySealRewardReceipt receipt,
            out string error);

        bool TryCommitVictorySealCashOut(
            string rewardContentId,
            int baseJade,
            int finalJade,
            out LevelVictorySealRewardReceipt receipt,
            out string error);
    }

    /// <summary>
    /// BOSS 单枚灵印奖励的原子提交边界。它拥有卡牌写入/回滚、精确实例校验、
    /// 化玉 exactly-once 门禁、严格保存与钱包恢复；UI 只提交意图。
    /// Run 聚合根负责其状态投影，楼层负责节点/phase，钱包负责外部账本。
    /// </summary>
    public sealed class DungeonRunVictorySealRewardTransaction
    {
        private readonly DungeonRunSession _run;
        private bool _committing;
        private IDungeonRunInteractionWallet _unrestoredWallet;
        private int _unrestoredBalance;

        // A failed participant recovery also blocks other Run transactions.
        internal bool IsCommitting => _committing || _unrestoredWallet != null;

        internal DungeonRunVictorySealRewardTransaction(DungeonRunSession run)
            => _run = run ?? throw new ArgumentNullException(nameof(run));
        public bool TryGetPending(out LevelVictorySealRewardOffer offer)
        {
            if (!_run.CurrentFloor.TryGetPendingVictorySealReward(out offer)) return false;
            if (!_run.CanReceiveSealRewards)
                offer = new LevelVictorySealRewardOffer(offer.NodeContentId, offer.RewardContentId,
                    Array.Empty<string>(), null, offer.PresentationId, true, LevelVictorySealRewardPhase.CashOutOnly);
            return true;
        }

        /// <summary>Persist must be atomic and throw if no save was committed. Null is for memory-only clients.</summary>
        public bool TryAssign(string rewardContentId, int cardInstanceId, CardSealSpec seal,
            IReadOnlyList<CardConfig> cardPool, out LevelVictorySealRewardReceipt receipt,
            out string error, Func<string, bool> isEvolutionResultEligible = null, Action persist = null)
        {
            receipt = default;
            if (!TryBegin(out error)) return false;
            try
            {
                if (_run.CurrentFloor.TryGetVictorySealRewardReceipt(rewardContentId, out _))
                    return _run.ApplyVictorySealAssignment(rewardContentId, cardInstanceId, seal,
                        cardPool, out receipt, out error, isEvolutionResultEligible);
                LevelVictorySealRewardReceipt resolved = default;
                _run.CommitRewardState(() =>
                {
                    if (!_run.ApplyVictorySealAssignment(rewardContentId, cardInstanceId, seal,
                            cardPool, out resolved, out string commitError, isEvolutionResultEligible) ||
                        !resolved.NewlyResolved)
                        throw new InvalidOperationException(commitError ?? "胜利灵印提交期间奖励状态已变化。");
                }, persist);
                receipt = resolved;
                return true;
            }
            catch (Exception exception)
            { error = "胜利钤印未提交，已恢复检查点：" + exception.Message; return false; }
            finally { _committing = false; }
        }

        public bool TryChooseSeal(string rewardContentId, string sealContentId,
            out string error, Action persist = null)
        {
            if (!_run.CanReceiveSealRewards) { error = "取得玉印后才能钤印。"; return false; }
            return TryCheckpoint(floor => floor.TryChooseVictorySeal(rewardContentId, sealContentId,
                out string failure) ? null : failure ?? "无法选择胜利灵印。", out error, persist);
        }

        public bool TryBeginAssignment(string rewardContentId, out string error, Action persist = null)
        {
            if (!_run.CanReceiveSealRewards) { error = "取得玉印后才能钤印。"; return false; }
            return TryCheckpoint(floor => floor.TryBeginVictorySealAssignment(rewardContentId,
                out string failure) ? null : failure ?? "无法进入胜利钤印。", out error, persist);
        }

        public bool TryReturnToDisposition(string rewardContentId, out string error, Action persist = null)
            => TryCheckpoint(floor => floor.TryReturnToVictorySealDisposition(rewardContentId,
                out string failure) ? null : failure ?? "无法返回胜利奖励处理。", out error, persist);

        public bool TryReturnToSealChoice(string rewardContentId, out string error, Action persist = null)
            => TryCheckpoint(floor => floor.TryReturnToVictorySealChoice(rewardContentId,
                out string failure) ? null : failure ?? "无法返回胜利灵印候选。", out error, persist);

        private bool TryCheckpoint(Func<LevelRunSession, string> apply, out string error, Action persist)
        {
            if (!TryBegin(out error)) return false;
            try
            {
                _run.CommitRewardState(() =>
                {
                    string failure = apply(_run.CurrentFloor);
                    if (failure != null) throw new InvalidOperationException(failure);
                }, persist);
                return true;
            }
            catch (Exception exception)
            { error = "胜利奖励步骤未保存，已恢复检查点：" + exception.Message; return false; }
            finally { _committing = false; }
        }

        /// <summary>Includes the wallet, Run income, reward receipt and atomic save in one synchronous commit.</summary>
        public bool TryCashOut(string rewardContentId, int baseAmount, IDungeonRunInteractionWallet wallet,
            out LevelVictorySealRewardReceipt receipt, out string error, Action persist = null)
        {
            receipt = default;
            if (!TryBegin(out error)) return false;
            int balance = 0;
            bool creditAttempted = false, succeeded = false;
            try
            {
                if (wallet == null || baseAmount < 0)
                    throw new InvalidOperationException("胜利化玉需要有效钱包和非负基础金额。");
                if (_run.CurrentFloor.TryGetVictorySealRewardReceipt(rewardContentId, out var prior))
                {
                    if (!prior.IsCashOut || prior.BaseJade != baseAmount)
                        throw new InvalidOperationException("胜利奖励已按另一方式或金额结算。");
                    receipt = prior;
                    succeeded = true;
                    return true;
                }
                if (!TryGetPending(out var pending) || pending.RewardContentId != rewardContentId ||
                    !pending.AllowsCashOut || (!pending.IsCashOutOnly &&
                        pending.Phase != LevelVictorySealRewardPhase.ChoosingDisposition && pending.Phase != LevelVictorySealRewardPhase.ChoosingSeal))
                    throw new InvalidOperationException(error ?? "胜利奖励不允许化玉。");
                balance = wallet.Balance;
                if (balance < 0) throw new InvalidOperationException("胜利化玉钱包余额无效。");
                LevelVictorySealRewardReceipt resolved = default;
                _run.CommitRewardState(() =>
                {
                    if (!_run.CanReceiveSealRewards) _run.CurrentFloor.RestrictPendingVictorySealToCashOut();
                    int runJadeBefore = _run.RunProgression.RunJadeEarned;
                    JadeGainResult expected = wallet.Preview(baseAmount);
                    if (expected.Source != JadeSource.CashOut || expected.BaseJade != baseAmount ||
                        expected.FinalJade < 0)
                        throw new InvalidOperationException("胜利化玉预览回执无效。");
                    RequireSamePending(pending);
                    creditAttempted = true;
                    JadeGainResult actual = wallet.Credit(baseAmount);
                    RequireSamePending(pending);
                    if (actual.Source != JadeSource.CashOut || actual.BaseJade != baseAmount ||
                        actual.FinalJade != expected.FinalJade || actual.FinalJade < 0 ||
                        actual.AffectedByGreed != expected.AffectedByGreed ||
                        (long)wallet.Balance - balance != actual.FinalJade ||
                        (long)_run.RunProgression.RunJadeEarned - runJadeBefore != actual.FinalJade)
                        throw new InvalidOperationException("胜利化玉账本未提交精确配置金额。");
                    if (!_run.CurrentFloor.TryCommitVictorySealCashOut(rewardContentId,
                            actual.BaseJade, actual.FinalJade, out resolved, out string commitError) ||
                        !resolved.NewlyResolved)
                        throw new InvalidOperationException(commitError ?? "胜利化玉奖励状态已变化。");
                }, persist);
                receipt = resolved;
                succeeded = true;
                return true;
            }
            catch (Exception exception)
            { error = "胜利化玉未提交，已恢复Run检查点：" + exception.Message; return false; }
            finally
            {
                if (!succeeded && creditAttempted && !TryRestoreWallet(wallet, balance, out string recoveryError))
                {
                    _unrestoredWallet = wallet;
                    _unrestoredBalance = balance;
                    error += " 钱包恢复未完成，后续提交已暂停：" + recoveryError;
                }
                _committing = false;
            }
        }

        // Existing headless clients use a memory-only grant. They cannot provide
        // durable account rollback; production uses the wallet overload above.
        internal bool TryCashOut(string rewardContentId, Func<JadeGainResult> grant,
            out LevelVictorySealRewardReceipt receipt, out string error)
        {
            receipt = default;
            if (!TryBegin(out error)) return false;
            try
            {
                if (_run.CurrentFloor.TryGetVictorySealRewardReceipt(rewardContentId, out _))
                    return TryCashOut(_run.CurrentFloor, rewardContentId, grant, out receipt, out error);
                LevelVictorySealRewardReceipt resolved = default;
                _run.CommitRewardState(() =>
                {
                    if (!_run.CanReceiveSealRewards) _run.CurrentFloor.RestrictPendingVictorySealToCashOut();
                    if (!TryCashOut(_run.CurrentFloor, rewardContentId, grant, out resolved, out string failure) ||
                        !resolved.NewlyResolved)
                        throw new InvalidOperationException(failure ?? "胜利化玉奖励状态已变化。");
                }, persist: null);
                receipt = resolved;
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally { _committing = false; }
        }

        private bool TryBegin(out string error)
        {
            error = null;
            if (_committing || _run.Interactions.IsCommitting || _run.GemChests.IsCommitting || _run.DoubleKeyPayments.IsCommitting ||
                _run.FloorExits.IsCommitting || _run.CardConversions.IsCommitting || _run.SideEvents.IsCommitting || _run.Crystals.IsCommitting || _run.Crystals.HasPendingEvent || (_run.Arcanas.IsCommitting || (_run.ChickenOfferings.IsCommitting || _run.ManaStatues.IsCommitting || _run.AbandonedCarts.IsCommitting) || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy)))
            { error = "Run奖励事务正在提交。"; return false; }
            _committing = true;
            if (_unrestoredWallet != null)
            {
                if (!TryRestoreWallet(_unrestoredWallet, _unrestoredBalance, out string recoveryError))
                {
                    _committing = false;
                    error = "先前胜利化玉钱包仍未恢复：" + recoveryError;
                    return false;
                }
                _unrestoredWallet = null;
            }
            return true;
        }

        private void RequireSamePending(LevelVictorySealRewardOffer expected)
        {
            if (!_run.CurrentFloor.TryGetPendingVictorySealReward(out var actual) ||
                actual.RewardContentId != expected.RewardContentId || actual.Phase != expected.Phase ||
                actual.SealContentId != expected.SealContentId || actual.IsCashOutOnly != expected.IsCashOutOnly)
                throw new InvalidOperationException("经济回调期间胜利奖励或已选灵印发生变化。");
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

        internal static bool TryAssign(
            List<DungeonRunCardSaveData> cards,
            ILevelVictorySealRewardCommitTarget commitTarget,
            string rewardContentId,
            int cardRuntimeInstanceId,
            CardSealSpec seal,
            IReadOnlyList<CardConfig> cardPool,
            out LevelVictorySealRewardReceipt receipt,
            out string error,
            Func<string, bool> isEvolutionResultEligible = null)
        {
            receipt = default;
            error = null;
            if (cards == null || commitTarget == null)
            {
                error = "胜利灵印事务缺少 Run 状态。";
                return false;
            }
            if (commitTarget.TryGetVictorySealRewardReceipt(
                    rewardContentId, out LevelVictorySealRewardReceipt prior))
            {
                if (prior.Disposition !=
                        LevelVictorySealRewardDisposition.Assigned ||
                    prior.CardRuntimeInstanceId != cardRuntimeInstanceId ||
                    seal == null ||
                    !string.Equals(
                        prior.SealContentId,
                        seal.contentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = $"胜利奖励 {rewardContentId} 已以其它方式结算。";
                    return false;
                }
                receipt = prior;
                return true;
            }
            if (!TryRequirePending(
                    commitTarget,
                    rewardContentId,
                    LevelVictorySealRewardPhase.AssigningSeal,
                    out LevelVictorySealRewardOffer pending,
                    out error))
                return false;
            if (seal == null || string.IsNullOrWhiteSpace(seal.contentId) ||
                !string.Equals(
                    pending.SealContentId,
                    seal.contentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = $"胜利奖励 {rewardContentId} 的灵印内容不匹配。";
                return false;
            }
            if (cardRuntimeInstanceId <= 0)
            {
                error = "钤印目标必须是有效的卡牌实例 ID。";
                return false;
            }

            int savedIndex = cards.FindIndex(card =>
                card != null &&
                card.runtimeInstanceId == cardRuntimeInstanceId);
            if (savedIndex < 0)
            {
                error = $"Run 牌组中找不到卡牌实例：" +
                        $"{cardRuntimeInstanceId}。";
                return false;
            }
            DungeonRunCardSaveData saved = cards[savedIndex];
            CardConfig source = DungeonRunSealAssignmentRules.FindSource(cardPool, saved.contentId);
            if (source == null)
            {
                error = $"Run 牌组引用不存在的卡牌：{saved.contentId}。";
                return false;
            }

            if (!DungeonRunSealAssignmentRules.TryPrepare(saved, source, seal,
                    id => DungeonRunSealAssignmentRules.FindSource(cardPool, id) != null &&
                        (isEvolutionResultEligible == null || isEvolutionResultEligible(id)),
                    out var replacement, out int slotIndex, out error))
                return false;
            // Eligibility callbacks must not silently change the selected reward.
            if (!TryRequirePending(commitTarget, rewardContentId,
                    LevelVictorySealRewardPhase.AssigningSeal, out var current, out error) ||
                !string.Equals(current.SealContentId, seal.contentId, StringComparison.OrdinalIgnoreCase))
            { error ??= "钤印校验期间已选灵印发生变化。"; return false; }
            cards[savedIndex] = replacement;
            try
            {
                if (!commitTarget.TryCommitVictorySealAssignment(rewardContentId,
                        cardRuntimeInstanceId, slotIndex, out receipt, out error))
                { cards[savedIndex] = saved; receipt = default; return false; }
                return true;
            }
            catch
            { cards[savedIndex] = saved; receipt = default; throw; }
        }

        internal static bool TryCashOut(
            ILevelVictorySealRewardCommitTarget commitTarget,
            string rewardContentId,
            Func<JadeGainResult> grantCashOut,
            out LevelVictorySealRewardReceipt receipt,
            out string error)
        {
            receipt = default;
            error = null;
            if (commitTarget == null || grantCashOut == null)
            {
                error = "胜利灵印化玉事务缺少状态或经济处理器。";
                return false;
            }
            if (commitTarget.TryGetVictorySealRewardReceipt(
                    rewardContentId, out LevelVictorySealRewardReceipt prior))
            {
                if (!prior.IsCashOut)
                {
                    error = $"胜利奖励 {rewardContentId} 已以其它方式结算。";
                    return false;
                }
                receipt = prior;
                return true;
            }
            if (!TryRequirePending(
                    commitTarget,
                    rewardContentId,
                    LevelVictorySealRewardPhase.ChoosingDisposition,
                    out LevelVictorySealRewardOffer pending,
                    out error))
                return false;
            if (!pending.AllowsCashOut)
            {
                error = $"胜利奖励 {rewardContentId} 不允许化玉。";
                return false;
            }

            JadeGainResult result;
            try
            {
                result = grantCashOut();
            }
            catch (Exception exception)
            {
                error = "胜利灵印化玉失败：" + exception.Message;
                return false;
            }
            if (result.Source != JadeSource.CashOut ||
                result.BaseJade < 0 || result.FinalJade < 0)
            {
                error = "胜利灵印化玉处理器返回了无效回执。";
                return false;
            }
            return commitTarget.TryCommitVictorySealCashOut(
                rewardContentId,
                result.BaseJade,
                result.FinalJade,
                out receipt,
                out error);
        }

        private static bool TryRequirePending(
            ILevelVictorySealRewardCommitTarget commitTarget,
            string rewardContentId,
            LevelVictorySealRewardPhase requiredPhase,
            out LevelVictorySealRewardOffer pending,
            out string error)
        {
            pending = default;
            error = null;
            if (!commitTarget.TryGetPendingVictorySealReward(out pending))
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
            // ChoosingDisposition requests here are cash-out commits; assignment
            // still requires the exact AssigningSeal phase and selected identity.
            if (pending.Phase != requiredPhase &&
                !(requiredPhase == LevelVictorySealRewardPhase.ChoosingDisposition &&
                  (pending.IsCashOutOnly || pending.Phase == LevelVictorySealRewardPhase.ChoosingSeal)))
            {
                error = $"胜利奖励 {rewardContentId} 当前步骤为 " +
                        $"{pending.Phase}，期望 {requiredPhase}。";
                return false;
            }
            return true;
        }

    }
}
