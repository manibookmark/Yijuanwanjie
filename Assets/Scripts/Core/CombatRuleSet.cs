using System;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 单场战斗使用的规则开关与可调参数。Meta/Content 只在 Bootstrap 中映射到这里，
    /// Core 不反向依赖秘法或遗物定义。
    /// </summary>
    [Serializable]
    public sealed class CombatRuleSet
    {
        [Range(0f, 1f)] public float knockbackBaseChance = 0.15f;
        [Range(0f, 1f)] public float disarmBaseChance = 0.10f;

        public bool preservePlayerArmorBetweenTurns;
        public bool shieldBashEnabled;
        /// <summary>回春秘药：所有治疗结算翻倍。</summary>
        public bool experimentalMedicineEnabled;

        /// <summary>叠势规则开关；内部 Combo API 为兼容既有代码继续保留。</summary>
        public bool comboEnabled = true;

        /// <summary>五行相生规则开关；正式关卡由五行盘遗珍门禁。</summary>
        public bool wuxingEnabled = true;

        public bool bossOverkillEnabled;
        public bool normalOverkillEnabled;
        public bool ultraOverkillEnabled;

        public CombatRuleSet Clone()
        {
            return (CombatRuleSet)MemberwiseClone();
        }
    }

    /// <summary>
    /// Exact nonnegative scalar Run counters. Child owners have their own snapshots;
    /// restoring this value never replaces them or applies encounter projections.
    /// </summary>
    public readonly struct RunProgressionSnapshot
    {
        public int LuckBonus { get; }
        public int GreedBonus { get; }
        public int JadeEarned { get; }
        public int GrowthBonus { get; }

        public RunProgressionSnapshot(int luckBonus, int greedBonus, int jadeEarned, int growthBonus)
        {
            if (luckBonus < 0) throw new ArgumentOutOfRangeException(nameof(luckBonus));
            if (greedBonus < 0) throw new ArgumentOutOfRangeException(nameof(greedBonus));
            if (jadeEarned < 0) throw new ArgumentOutOfRangeException(nameof(jadeEarned));
            if (growthBonus < 0) throw new ArgumentOutOfRangeException(nameof(growthBonus));
            LuckBonus = luckBonus; GreedBonus = greedBonus;
            JadeEarned = jadeEarned; GrowthBonus = growthBonus;
        }
    }

    /// <summary>
    /// 一局游戏内跨战斗保留的成长状态。基础天运/敛玉/明悟仍存于 PlayerStats，
    /// 这里保存本局通过卡牌和灵印获得的增量；百术周天的冻结配置、
    /// 真实出牌账本与独立灵力来源由 CardPlayGrowth 唯一拥有。
    /// </summary>
    [Serializable]
    public sealed class RunProgressionState
    {
        public const int DefaultGreedCap = 1000;
        public const int DefaultGrowthCap = 1000;
        public const int DefaultLuckSafetyCap = 1_000_000;

        public int RunLuckBonus { get; private set; }
        public int RunGreedBonus { get; private set; }
        public int RunGrowthBonus { get; private set; }
        public int RunJadeEarned { get; private set; }

        /// <summary>Never consult live account state to enable an in-progress Run.</summary>
        public RunCardPlayGrowth CardPlayGrowth { get; private set; }

        /// <summary>Real behavior facts are always tracked, independently of equipped arcana.</summary>
        public RunCardUnlockFacts CardUnlockFacts { get; private set; } = new RunCardUnlockFacts();

        /// <summary>One-use Duration reserve survives encounters independently of PlayerStats.</summary>
        public CrawlerDurationReserve CrawlerDurationReserve { get; private set; } = new CrawlerDurationReserve();

        /// <summary>Permanent Amount/Magnet/Curse deltas, independent of encounter stats.</summary>
        public RunAdditionalStatModifiers AdditionalStats { get; private set; } = new RunAdditionalStatModifiers();
        /// <summary>Remaining charge and exact consumed lives for this Run only.</summary>
        public RunRevivalReserve RevivalReserve { get; private set; } = new RunRevivalReserve();

        public RunProgressionState() : this(false) { }

        public RunProgressionState(bool cardPlayGrowthEnabled)
        {
            CardPlayGrowth = new RunCardPlayGrowth(cardPlayGrowthEnabled);
        }

        public int AddLuck(int amount, int safetyCap = DefaultLuckSafetyCap)
        {
            if (amount <= 0) return 0;
            int cap = Math.Max(0, safetyCap);
            int before = RunLuckBonus;
            if (before >= cap) return 0;
            int gain = (int)Math.Min((long)amount, (long)cap - before);
            RunLuckBonus = before + gain;
            return gain;
        }

        public int AddGreed(int amount, int runCap = DefaultGreedCap)
        {
            if (amount <= 0) return 0;
            int cap = Math.Max(0, runCap);
            int before = RunGreedBonus;
            if (before >= cap) return 0;
            int gain = (int)Math.Min((long)amount, (long)cap - before);
            RunGreedBonus = before + gain;
            return gain;
        }

        public int AddGrowth(int amount, int runCap = DefaultGrowthCap)
        {
            if (amount <= 0) return 0;
            int cap = Math.Max(0, runCap);
            int before = RunGrowthBonus;
            if (before >= cap) return 0;
            int gain = (int)Math.Min((long)amount, (long)cap - before);
            RunGrowthBonus = before + gain;
            return gain;
        }

        /// <summary>
        /// Full integer source grants retain Base deltas independently of ordinary
        /// pickup capacities. This project storage is not native binary32 arithmetic.
        /// An unrepresentable request fails before any scalar is changed.
        /// </summary>
        public int AddSourceLuck(int amount)
        {
            int next = AddExactSource(RunLuckBonus, amount);
            RunLuckBonus = next;
            return amount;
        }

        public int AddSourceGreed(int amount)
        {
            int next = AddExactSource(RunGreedBonus, amount);
            RunGreedBonus = next;
            return amount;
        }

        public int AddSourceGrowth(int amount)
        {
            int next = AddExactSource(RunGrowthBonus, amount);
            RunGrowthBonus = next;
            return amount;
        }

        private static int AddExactSource(int owned, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            return checked(owned + amount);
        }

        // Luck and Greed preserve the project's existing total-value semantics:
        // ordinary caps limit Run increments, not the sum with the entry baseline.
        public int ProjectLuck(int baseline) => Project(baseline, RunLuckBonus, int.MaxValue);
        public int ProjectGreed(int baseline) => Project(baseline, RunGreedBonus, int.MaxValue);
        public int ProjectGrowth(int baseline) => Project(baseline, RunGrowthBonus, DefaultGrowthCap);
        private static int Project(int baseline, int bonus, int cap)
            => (int)Math.Max(0L, Math.Min((long)cap, (long)baseline + bonus));

        public RunProgressionSnapshot Capture()
            => new RunProgressionSnapshot(RunLuckBonus, RunGreedBonus, RunJadeEarned, RunGrowthBonus);

        /// <summary>Restore exact raw counters; no grant events or child-owner replacement.</summary>
        public void Restore(RunProgressionSnapshot snapshot)
        {
            RunLuckBonus = snapshot.LuckBonus;
            RunGreedBonus = snapshot.GreedBonus;
            RunJadeEarned = snapshot.JadeEarned;
            RunGrowthBonus = snapshot.GrowthBonus;
        }

        public void RecordJade(int amount)
        {
            if (amount > 0) RunJadeEarned += amount;
        }

        /// <summary>Legacy scalar restore retains its existing Growth cap; modern raw owners use Restore(snapshot).</summary>
        public void Restore(
            int luckBonus,
            int greedBonus,
            int jadeEarned,
            int growthBonus = 0)
        {
            RunLuckBonus = Math.Max(0, luckBonus);
            RunGreedBonus = Math.Max(0, greedBonus);
            RunGrowthBonus = Math.Max(
                0,
                Math.Min(DefaultGrowthCap, growthBonus));
            RunJadeEarned = Math.Max(0, jadeEarned);
        }

        public void ResetRun()
        {
            RunLuckBonus = 0;
            RunGreedBonus = 0;
            RunGrowthBonus = 0;
            RunJadeEarned = 0;
            // A new Run needs a fresh admission decision. Retaining the previous
            // enabled flag would enable legacy/debug entries without admission.
            CardPlayGrowth = new RunCardPlayGrowth();
            CardUnlockFacts = new RunCardUnlockFacts();
            CrawlerDurationReserve = new CrawlerDurationReserve();
            AdditionalStats = new RunAdditionalStatModifiers();
            RevivalReserve = new RunRevivalReserve();
        }
    }
}
