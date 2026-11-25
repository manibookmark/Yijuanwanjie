using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Immutable result of one successful payment. Post-play rules use this
    /// snapshot, never the balance after the host card has restored mana.
    /// A default receipt represents no positive payment and grants no refund.
    /// </summary>
    public readonly struct ManaPaymentReceipt
    {
        internal ManaPaymentReceipt(int paidAmount, long availableAfterPayment,
            int creditedAmount = 0)
        {
            PaidAmount = paidAmount;
            AvailableAfterPayment = availableAfterPayment;
            CreditedAmount = creditedAmount;
        }

        /// <summary>Positive expenditure only; negative costs never qualify for refund seals.</summary>
        public int PaidAmount { get; }
        /// <summary>Actual temporary mana credited by a negative payment, after the wallet cap.</summary>
        public int CreditedAmount { get; }
        public long AvailableAfterPayment { get; }
        public bool ExhaustedAvailableMana =>
            PaidAmount > 0 && AvailableAfterPayment == 0;
    }

    /// <summary>
    /// 法力系统
    /// 每回合有限法力，临时法力回合末失效
    /// 首发不做跨回合保留
    /// </summary>
    public class ManaSystem
    {
        /// <summary>
        /// 基础法力上限（每回合恢复到此值）
        /// </summary>
        public int BaseMana { get; private set; }

        /// <summary>
        /// 当前可用法力
        /// </summary>
        public int CurrentMana { get; private set; }

        /// <summary>
        /// 临时法力（回合末失效）
        /// </summary>
        public int TemporaryMana { get; private set; }

        public ManaSystem(int baseMana)
        {
            BaseMana = baseMana;
            CurrentMana = baseMana;
            TemporaryMana = 0;
        }

        /// <summary>
        /// 尝试消耗法力出牌
        /// </summary>
        public bool TrySpend(int cost)
            => TrySpend(cost, out _);

        /// <summary>
        /// Pays from temporary mana first and captures the total remaining
        /// balance before any card effect. Failed/free payments have a zero
        /// paid amount, so they cannot trigger a refund or exhaustion rebate.
        /// A negative payment credits temporary mana exactly once and records
        /// the actual credit independently of positive expenditure.
        /// </summary>
        public bool TrySpend(int cost, out ManaPaymentReceipt receipt)
        {
            if (!PreviewPayment(cost, out receipt))
                return false;
            if (cost <= 0)
            {
                if (receipt.CreditedAmount > 0)
                    AddTemporaryMana(receipt.CreditedAmount);
                return true;
            }

            // 先消耗临时法力
            int tempUsed = Math.Min(TemporaryMana, cost);
            TemporaryMana -= tempUsed;
            CurrentMana -= (cost - tempUsed);
            return true;
        }

        /// <summary>Uses the payment rule without changing either mana balance.</summary>
        public bool PreviewPayment(int cost, out ManaPaymentReceipt receipt)
        {
            receipt = default;
            long available = (long)CurrentMana + TemporaryMana;
            if (cost <= 0)
            {
                int credit = cost < 0
                    ? (int)Math.Min(-(long)cost,
                        (long)int.MaxValue - TemporaryMana)
                    : 0;
                receipt = new ManaPaymentReceipt(0, available + credit, credit);
                return true;
            }
            if (available < cost) return false;
            receipt = new ManaPaymentReceipt(cost, available - cost);
            return true;
        }

        /// <summary>
        /// 添加基础法力（Power-Up / 遗物 / Run成长），不回填当前余额。
        /// </summary>
        public void AddBaseMana(int amount)
        {
            BaseMana = (int)Math.Max(0L, Math.Min(int.MaxValue, (long)BaseMana + amount));
        }

        /// <summary>
        /// 添加临时法力（灵卡效果，回合末失效）
        /// </summary>
        public void AddTemporaryMana(int amount)
        {
            TemporaryMana = (int)Math.Max(0L,
                Math.Min(int.MaxValue, (long)TemporaryMana + amount));
        }

        /// <summary>
        /// 回合开始：恢复法力至基础值，清空临时法力
        /// </summary>
        public void OnTurnStart()
        {
            CurrentMana = BaseMana;
            TemporaryMana = 0;
        }

        public override string ToString()
        {
            return TemporaryMana > 0
                ? $"法力: {CurrentMana}(+{TemporaryMana}) / {BaseMana}"
                : $"法力: {CurrentMana} / {BaseMana}";
        }
    }
}
