using System;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns direct seal cash-out commits and economy recovery. Account permission is borrowed
    /// from the Run; card state, floor receipts and money keep their existing owners.
    /// A locked source chest records this explicit disposition without inventing generated Gem proof.
    /// </summary>
    internal sealed class DungeonRunSealRewardPolicy
    {
        internal const string LockedCashOutTarget = "reward.seal.permission-locked.cash-out.v1";
        private readonly DungeonRunSession _run;
        private bool _committing;
        private IDungeonRunInteractionWallet _unrestoredWallet;
        private int _unrestoredBalance;
        internal bool IsCommitting => _committing || _unrestoredWallet != null;
        internal DungeonRunSealRewardPolicy(DungeonRunSession run) => _run = run;
        internal bool TryRecoverWallet(out string error)
        {
            error = null;
            if (_committing) { error = "灵印奖励正在结算。"; return false; }
            if (_unrestoredWallet == null) return true;
            _committing = true;
            try
            {
                if (!RestoreWallet(_unrestoredWallet, _unrestoredBalance, out error)) return false;
                _unrestoredWallet = null; return true;
            }
            finally { _committing = false; }
        }

        internal static bool IsLockedCashOut(LevelInteractionEffect effect, LevelInteractionReceipt receipt)
            => effect != null && receipt.Kind == effect.kind && receipt.EffectId == effect.effectId &&
               receipt.TargetContentId == LockedCashOutTarget && receipt.TargetRuntimeInstanceId == 0 &&
               receipt.Amount >= 0 && receipt.ValueBefore >= 0 &&
               (long)receipt.ValueAfter - receipt.ValueBefore == receipt.Amount;

        internal bool TryCashOut(string nodeId, LevelInteractionEffect effect, int baseAmount,
            IDungeonRunInteractionWallet wallet, out LevelInteractionReceipt receipt, out string error, Action persist)
        {
            receipt = default; error = null;
            if (_committing) { error = "灵印奖励正在结算。"; return false; }
            if (_unrestoredWallet != null)
            {
                _committing = true;
                try
                {
                    if (!RestoreWallet(_unrestoredWallet, _unrestoredBalance, out error)) return false;
                    _unrestoredWallet = null;
                }
                finally { _committing = false; }
            }
            var floor = _run.CurrentFloor;
            if (effect == null || (effect.kind != LevelInteractionEffectKind.GrantSeal && effect.kind != LevelInteractionEffectKind.GemChest) ||
                !floor.MatchesConfiguredEffect(nodeId, effect) || baseAmount != 200)
            { error = "化玉需要有效的灵印奖励配置。"; return false; }
            if (floor.TryGetInteractionReceipt(effect.effectId, out receipt))
            {
                if (IsLockedCashOut(effect, receipt)) return true;
                error = "该奖励已按其他去向结算。"; return false;
            }
            if (effect.kind == LevelInteractionEffectKind.GemChest && _run.CanReceiveSealRewards)
            { error = "已开放的印匣应通过原印匣事务结算。"; return false; }
            if (_run.IsRewardCommitInProgress || _run.Interactions.HasPendingChoice || _run.GemChests.HasPendingChoice ||
                _run.CurrentFloor.ActiveEncounter.HasValue || _run.CurrentFloor.TryGetPendingReturn(out _) ||
                _run.TryGetConclusion(out _) || _run.IsSpecializedInteractionBusy)
            { error = "请先完成当前奖励事务。"; return false; }
            if (wallet == null) { error = "化玉缺少经济账本。"; return false; }
            int balance = wallet.Balance;
            bool touched = false;
            _committing = true;
            try
            {
                LevelInteractionReceipt committed = default;
                _run.CommitRewardState(() =>
                {
                    if (floor.ActiveInteractionNodeId != nodeId && !floor.TryBeginInteraction(nodeId, out var beginError))
                        throw new InvalidOperationException(beginError);
                    int previousRunJade = _run.RunProgression.RunJadeEarned;
                    JadeGainResult expected = wallet.Preview(baseAmount);
                    if (balance < 0 || expected.Source != JadeSource.CashOut || expected.BaseJade != baseAmount || expected.FinalJade < 0)
                        throw new InvalidOperationException("化玉预告金额无效。");
                    touched = true;
                    JadeGainResult gain = wallet.Credit(baseAmount);
                    if (gain.Source != JadeSource.CashOut || gain.AffectedByGreed != expected.AffectedByGreed ||
                        gain.BaseJade != baseAmount || gain.FinalJade != expected.FinalJade ||
                        (long)wallet.Balance - balance != gain.FinalJade ||
                        (long)_run.RunProgression.RunJadeEarned - previousRunJade != gain.FinalJade)
                        throw new InvalidOperationException("化玉账本与实际收益不一致。");
                    if (!floor.TryRecordExternalEffect(nodeId, effect, balance, wallet.Balance, gain.FinalJade,
                        out committed, out var recordError, resolvedTargetContentId: LockedCashOutTarget))
                        throw new InvalidOperationException(recordError);
                    if (effect.kind == LevelInteractionEffectKind.GemChest && !floor.TryCompleteInteraction(nodeId, out var completeError))
                        throw new InvalidOperationException(completeError);
                }, () =>
                {
                    string checkpoint = _run.SerializeForDeterministicComparison(); int after = wallet.Balance;
                    persist?.Invoke();
                    if (!ReferenceEquals(floor, _run.CurrentFloor) || !floor.MatchesConfiguredEffect(nodeId, effect) ||
                        _run.SerializeForDeterministicComparison() != checkpoint || wallet.Balance != after)
                        throw new InvalidOperationException("保存期间灵印化玉状态发生变化。");
                });
                receipt = committed; return true;
            }
            catch (Exception exception)
            {
                error = "化玉未提交，已恢复检查点：" + exception.Message;
                if (touched && !RestoreWallet(wallet, balance, out string restoreError))
                { _unrestoredWallet = wallet; _unrestoredBalance = balance; error += " 钱包恢复未完成：" + restoreError; }
                return false;
            }
            finally { _committing = false; }
        }
        private static bool RestoreWallet(IDungeonRunInteractionWallet wallet, int balance, out string error)
        {
            try
            {
                wallet.RestoreBalance(balance);
                if (wallet.Balance != balance) throw new InvalidOperationException("钱包未恢复原余额。");
                error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
    }
}
