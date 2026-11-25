using System;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 地图灵玉奖励的唯一入账边界：把配置中的基础值交给 EconomySystem，
    /// 再同步局外余额、跨场景余额与本 Run 收益。调用方不复制敛玉公式。
    /// </summary>
    public sealed class LevelMapJadeRewardLedger : IDungeonRunFlatCashOutWallet
    {
        private readonly MetaProgression _meta;
        private readonly GameSession _gameSession;
        private readonly Func<int> _getCurrentGreed;
        private readonly EconomySystem _economy;
        private readonly JadeSource _transactionSource;
        private int _walletBalanceBefore, _walletTotalBefore, _walletViewBefore;
        private bool _canRollbackWalletCredit;

        public int CurrentJade => _meta.lingyu;
        public int CashOutBaseJade =>
            Math.Max(0, _economy.Config.cashOutJade);

        public LevelMapJadeRewardLedger(
            MetaProgression meta,
            GameSession gameSession,
            RunProgressionState runState,
            Func<int> getCurrentGreed,
            EconomyConfig config = null,
            JadeSource transactionSource = JadeSource.CashOut)
        {
            _meta = meta ?? throw new ArgumentNullException(nameof(meta));
            _gameSession = gameSession ??
                throw new ArgumentNullException(nameof(gameSession));
            _getCurrentGreed = getCurrentGreed ?? (() => 0);
            _transactionSource = transactionSource;
            _economy = new EconomySystem(
                config,
                runState ?? throw new ArgumentNullException(nameof(runState)),
                CreditFinalJade);
        }

        /// <summary>结算基础灵玉并返回结算后的总余额。</summary>
        public int Grant(int baseAmount)
        {
            _economy.Grant(
                baseAmount,
                Math.Max(0, _getCurrentGreed()),
                JadeSource.Special,
                affectedByGreed: true);
            return CurrentJade;
        }

        /// <summary>只预览当前敛玉下的化玉到账值，不修改任何账本。</summary>
        public int PreviewCashOut()
            => PreviewGrant(CashOutBaseJade);

        /// <summary>预览任意基础灵玉在当前敛玉下的实际到账值。</summary>
        public int PreviewGrant(int baseAmount)
        {
            return EconomySystem.CalculateFinalJade(
                Math.Max(0, baseAmount),
                Math.Max(0, _getCurrentGreed()),
                affectedByGreed: true);
        }

        /// <summary>按统一 EconomyConfig.cashOutJade 完成一次化玉结算。</summary>
        public JadeGainResult CashOut()
        {
            return _economy.CashOut(
                Math.Max(0, _getCurrentGreed()));
        }

        int IDungeonRunInteractionWallet.Balance => CurrentJade;

        JadeGainResult IDungeonRunInteractionWallet.Preview(int baseAmount)
            => new JadeGainResult(
                _transactionSource, Math.Max(0, baseAmount),
                PreviewGrant(baseAmount), affectedByGreed: true);

        JadeGainResult IDungeonRunInteractionWallet.Credit(int baseAmount)
            => CreditWallet(baseAmount, _transactionSource, affectedByGreed: true);

        public JadeGainResult PreviewFlatCashOut(int baseAmount)
        {
            if (baseAmount < 0) throw new ArgumentOutOfRangeException(nameof(baseAmount));
            return new JadeGainResult(JadeSource.CashOut, baseAmount, baseAmount, false);
        }

        public JadeGainResult CreditFlatCashOut(int baseAmount)
            => CreditWallet(baseAmount, JadeSource.CashOut, affectedByGreed: false);

        private JadeGainResult CreditWallet(int baseAmount, JadeSource source, bool affectedByGreed)
        {
            // A failed new attempt must never retain rollback authority for an
            // earlier successful credit. Validate every destination before Grant.
            _canRollbackWalletCredit = false;
            if (!affectedByGreed && baseAmount < 0)
                throw new ArgumentOutOfRangeException(nameof(baseAmount));
            int greed = affectedByGreed ? Math.Max(0, _getCurrentGreed()) : 0;
            int final = EconomySystem.CalculateFinalJade(baseAmount, greed, affectedByGreed);
            if (final < 0) throw new InvalidOperationException("Reward exceeds the supported jade range.");
            checked
            {
                _ = _meta.lingyu + final;
                _ = _meta.totalLingyuEarned + final;
                _ = _economy.RunState.RunJadeEarned + final;
            }
            _walletBalanceBefore = _meta.lingyu;
            _walletTotalBefore = _meta.totalLingyuEarned;
            _walletViewBefore = _gameSession.PlayerLingyu;
            _canRollbackWalletCredit = true;
            return _economy.Grant(
                baseAmount, greed, source, affectedByGreed, recordInRun: true);
        }

        void IDungeonRunInteractionWallet.RestoreBalance(int balance)
        {
            if (balance < 0)
                throw new ArgumentOutOfRangeException(nameof(balance));
            if (!_canRollbackWalletCredit || balance != _walletBalanceBefore)
            {
                // Validation can fail before Credit; that attempt changed no
                // account fields and must not undo an earlier successful grant.
                if (balance == _meta.lingyu) { _canRollbackWalletCredit = false; return; }
                throw new InvalidOperationException("Wallet rollback does not match its latest credit.");
            }
            // Dungeon restores its Run counters; this participant restores its
            // account balance, lifetime income and separate scene balance view.
            _meta.lingyu = _walletBalanceBefore;
            _meta.totalLingyuEarned = _walletTotalBefore;
            _gameSession.PlayerLingyu = _walletViewBefore;
            _canRollbackWalletCredit = false;
        }

        /// <summary>
        /// 按 VC Dungeon Bonus Coins 的边界发放关区通关灵玉：固定读取
        /// Dungeon 配置值，不受敛玉影响，也不写入关内收集灵玉统计。
        /// </summary>
        public JadeGainResult GrantDungeonCompletion(int baseAmount)
        {
            return _economy.Grant(
                Math.Max(0, baseAmount),
                currentGreed: 0,
                JadeSource.StageClear,
                affectedByGreed: false,
                recordInRun: false);
        }

        private void CreditFinalJade(int finalAmount)
        {
            _meta.CreditLingyu(finalAmount);
            _gameSession.PlayerLingyu = _meta.lingyu;
        }
    }
}
