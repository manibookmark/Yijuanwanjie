using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 一次已结算列补打的不可变回执。Targets 为命中前逻辑位置，表现不再
    /// 查询已死亡/补位的怪物；后续标准 DamageEvent 描述相同伤害，不再扣血。
    /// </summary>
    public sealed class ColumnReplayAttackEvent
    {
        public string SourceCardContentId { get; }
        public int SourceCardInstanceId => Plan.SourceCardInstanceId;
        public int Turn { get; }
        public AttackPlan Plan { get; }
        public AttackResolutionResult Resolution { get; }
        public IReadOnlyList<CombatEffectTarget> Targets { get; }

        internal ColumnReplayAttackEvent(string contentId, int turn, AttackPlan plan,
            AttackResolutionResult resolution, IReadOnlyList<CombatEffectTarget> targets)
        {
            SourceCardContentId = contentId;
            Turn = turn;
            Plan = plan;
            Resolution = resolution;
            Targets = targets;
        }
    }
}
