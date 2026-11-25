using System;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns the bridge between this sample's earned-Run receipts and its real
    /// isolated account balance. The two quantities are distinct: spending may
    /// change balance without changing cumulative Run income. EconomySystem
    /// still owns every grant formula; this adapter never recalculates a grant.
    /// </summary>
    internal sealed class RunAcceptanceEconomy
    {
        private readonly RunProgressionState _run;
        private int _creditedRunIncome;
        internal int CreditedRunIncome => _creditedRunIncome;
        internal MetaProgression Account { get; }

        internal RunAcceptanceEconomy(RunProgressionState run, MetaProgression account)
        { _run = run ?? throw new ArgumentNullException(nameof(run)); Account = account ?? throw new ArgumentNullException(nameof(account)); }

        internal void Synchronize()
        {
            int earned = _run.RunJadeEarned;
            if (earned < _creditedRunIncome)
                throw new InvalidOperationException("Run income moved backwards outside an owned rollback.");
            if (earned > _creditedRunIncome)
            {
                int delta = earned - _creditedRunIncome;
                checked { _ = Account.lingyu + delta; _ = Account.totalLingyuEarned + delta; }
                Account.CreditLingyu(delta);
            }
            _creditedRunIncome = earned;
        }

        internal IDungeonRunInteractionWallet ForGreed(int greed, JadeSource source = JadeSource.CashOut)
        { Synchronize(); return new Wallet(this, greed, source); }

        private sealed class Wallet : IDungeonRunFlatCashOutWallet
        {
            private readonly RunAcceptanceEconomy _owner;
            private readonly EconomySystem _economy;
            private readonly int _greed;
            private readonly JadeSource _source;
            private int _balanceBefore, _earnedBefore, _cursorBefore;
            private bool _canRollback;
            public int Balance => _owner.Account.lingyu;

            internal Wallet(RunAcceptanceEconomy owner, int greed, JadeSource source)
            {
                _owner = owner; _greed = Math.Max(0, greed); _source = source;
                _economy = new EconomySystem(new EconomyConfig(), owner._run, owner.Account.CreditLingyu);
            }
            public JadeGainResult Preview(int amount) => new JadeGainResult(_source, amount,
                EconomySystem.CalculateFinalJade(amount, _greed, true), true);
            public JadeGainResult Credit(int amount)
                => CreditWallet(amount, _source, affectedByGreed: true);
            public JadeGainResult PreviewFlatCashOut(int baseAmount)
            {
                if (baseAmount < 0) throw new ArgumentOutOfRangeException(nameof(baseAmount));
                return new JadeGainResult(JadeSource.CashOut, baseAmount, baseAmount, false);
            }
            public JadeGainResult CreditFlatCashOut(int baseAmount)
                => CreditWallet(baseAmount, JadeSource.CashOut, affectedByGreed: false);
            private JadeGainResult CreditWallet(int amount, JadeSource source, bool affectedByGreed)
            {
                _canRollback = false;
                if (!affectedByGreed && amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
                int final = EconomySystem.CalculateFinalJade(amount, _greed, affectedByGreed);
                if (final < 0) throw new InvalidOperationException("Reward exceeds the supported jade range.");
                int earned = _owner._run.RunJadeEarned;
                if (earned < _owner._creditedRunIncome)
                    throw new InvalidOperationException("Run income moved backwards outside an owned rollback.");
                int uncredited = earned - _owner._creditedRunIncome;
                checked
                {
                    _ = Balance + uncredited + final;
                    _ = _owner.Account.totalLingyuEarned + uncredited + final;
                    _ = earned + final;
                }
                // Synchronization belongs to this credit attempt too; rollback
                // restores its cursor and account without replaying income events.
                _balanceBefore = Balance; _earnedBefore = _owner.Account.totalLingyuEarned;
                _cursorBefore = _owner._creditedRunIncome; _canRollback = true;
                _owner.Synchronize();
                JadeGainResult receipt = _economy.Grant(amount, affectedByGreed ? _greed : 0,
                    source, affectedByGreed, recordInRun: true);
                _owner._creditedRunIncome = _owner._run.RunJadeEarned;
                return receipt;
            }
            public void RestoreBalance(int balance)
            {
                if (balance < 0) throw new ArgumentOutOfRangeException(nameof(balance));
                if (!_canRollback || balance != _balanceBefore)
                {
                    if (balance == Balance) { _canRollback = false; return; }
                    throw new InvalidOperationException("Wallet rollback does not match its most recent transaction.");
                }
                // Formal Dungeon restores the Run receipt in the same stack.
                // Restore account fields without publishing a second income event.
                _owner.Account.lingyu = _balanceBefore;
                _owner.Account.totalLingyuEarned = _earnedBefore;
                _owner._creditedRunIncome = _cursorBefore;
                _canRollback = false;
            }
        }
    }
}
