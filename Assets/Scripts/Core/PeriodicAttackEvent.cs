using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum PeriodicAttackChangeKind { Placed, Refreshed, Triggered, Removed }
    public enum PeriodicAttackSourceKind { FrozenPlan = 0, Bird = 1 }

    /// <summary>规则批次开始时的逻辑占格；异步消费者不再查询已死亡或补位的 roster。</summary>
    public readonly struct CombatEffectTarget
    {
        public int RuntimeId { get; }
        public int TargetIndex { get; }
        public int Row { get; }
        public int Column { get; }
        public int Width { get; }
        public int Depth { get; }

        internal CombatEffectTarget(EnemyStats enemy, int index)
        {
            RuntimeId = enemy.runtimeId;
            TargetIndex = index;
            Row = enemy.row;
            Column = enemy.column;
            Width = enemy.GridWidth;
            Depth = enemy.GridDepth;
        }
    }

    /// <summary>
    /// 不可变来源状态。FrozenPlan保存出牌计划；Bird保存周期基础模板与独立鸟身份。
    /// Triggered事件的Bird Snapshot是当次当前属性计划，驻场Active仍保留原模板；
    /// 作者配置不能倒灌，来源与回合计次只能由PeriodicAttackSystem替换。
    /// </summary>
    public sealed class PeriodicAttackSource
    {
        public int EffectRuntimeId { get; }
        public string SourceCardContentId { get; }
        public int SourceCardInstanceId => Snapshot.SourceCardInstanceId;
        public int LastTriggeredTurn { get; }
        public AttackPlan Snapshot { get; }
        public PeriodicAttackSourceKind Kind { get; }
        public bool IsBird => Kind == PeriodicAttackSourceKind.Bird;
        public int CastRuntimeId { get; }
        public int BirdIndex { get; }
        public int BirdCount { get; }
        public ResolvedBirdAttackSpec BirdAttack { get; }

        internal PeriodicAttackSource(int id, string contentId, AttackPlan snapshot, int lastTurn,
            PeriodicAttackSourceKind kind = PeriodicAttackSourceKind.FrozenPlan,
            int castRuntimeId = 0, int birdIndex = 0, int birdCount = 0,
            ResolvedBirdAttackSpec birdAttack = null)
        {
            EffectRuntimeId = id;
            SourceCardContentId = contentId ?? string.Empty;
            Snapshot = snapshot;
            LastTriggeredTurn = lastTurn;
            Kind = kind;
            CastRuntimeId = castRuntimeId;
            BirdIndex = birdIndex;
            BirdCount = birdCount;
            BirdAttack = birdAttack;
        }
    }

    /// <summary>
    /// Placed/Refreshed 仅登记；Triggered 携带一次权威批次及命中前位置，空目标不发 Triggered。
    /// 来源持续到 Removed，无倒计时；恢复应读取 Active，不重播 Placed/Triggered。
    /// </summary>
    public sealed class PeriodicAttackEvent
    {
        public PeriodicAttackChangeKind Kind { get; }
        public PeriodicAttackSource Source { get; }
        public int Turn { get; }
        public AttackResolutionResult Resolution { get; }
        public IReadOnlyList<CombatEffectTarget> Targets { get; }

        internal PeriodicAttackEvent(PeriodicAttackChangeKind kind, PeriodicAttackSource source,
            int turn, AttackResolutionResult resolution = null,
            IReadOnlyList<CombatEffectTarget> targets = null)
        {
            Kind = kind;
            Source = source;
            Turn = turn;
            Resolution = resolution;
            Targets = targets ?? Array.Empty<CombatEffectTarget>();
        }
    }
}
