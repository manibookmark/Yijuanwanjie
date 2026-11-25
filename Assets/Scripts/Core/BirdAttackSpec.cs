using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 鸟专用攻击作者规格。鸟数属于Pattern基础攻击物数；本规格只声明每鸟导弹，
    /// 不持有召唤实例、当前属性或回合状态。null规格代表既有普通攻击合同。
    /// </summary>
    [Serializable]
    public sealed class BirdAttackSpec
    {
        public int missilesPerBird = 2;
        public int endTurnMissilesPerBird = 5;
        public int endTurnDamagePerHit = 8;
        // Project splash-damage coefficient; VC area_size_coefficient is not
        // evidence of an equivalent damage multiplier.
        public float endTurnAreaCoefficient = 0.2f;

        public BirdAttackSpec Clone() => new BirdAttackSpec
        {
            missilesPerBird = missilesPerBird,
            endTurnMissilesPerBird = endTurnMissilesPerBird,
            endTurnDamagePerHit = endTurnDamagePerHit,
            endTurnAreaCoefficient = endTurnAreaCoefficient
        };

        internal void RequireValid()
        {
            if (missilesPerBird <= 0 || endTurnMissilesPerBird <= 0 || endTurnDamagePerHit <= 0)
                throw new ArgumentException("Bird attack missile counts and damage must be positive.");
            if (float.IsNaN(endTurnAreaCoefficient) || float.IsInfinity(endTurnAreaCoefficient) ||
                endTurnAreaCoefficient < 0f)
                throw new ArgumentException("Bird end-turn area coefficient must be finite and non-negative.");
        }
    }

    /// <summary>召唤时复制的不可变鸟规格；当前Might/Area由周期owner在触发时另行读取。</summary>
    public sealed class ResolvedBirdAttackSpec
    {
        public int MissilesPerBird { get; }
        public int EndTurnMissilesPerBird { get; }
        public int EndTurnDamagePerHit { get; }
        public float EndTurnAreaCoefficient => Area.coefficient;
        public ResolvedAreaSpec Area { get; }

        internal ResolvedBirdAttackSpec(BirdAttackSpec source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            source.RequireValid();
            MissilesPerBird = source.missilesPerBird;
            EndTurnMissilesPerBird = source.endTurnMissilesPerBird;
            EndTurnDamagePerHit = source.endTurnDamagePerHit;
            // 周期导弹有独立的范围合同，不继承原卡初击的范围系数或开关。
            Area = new ResolvedAreaSpec(new AreaSpec { enabled = true,
                coefficient = source.endTurnAreaCoefficient, triggerMode = AreaTriggerMode.PerSubHit });
        }
    }
}
