using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 战斗事件类型。用于统一事件流的分类和路由。
    /// </summary>
    public enum CombatEventType
    {
        TurnStarted,
        CardsDrawn,
        CardPlayed,
        CardPlayedVFX,
        CardResolved,
        CardResolvedFull,
        EnemyDamaged,
        EnemyKilled,
        EnemyKnockedBack,
        FrontRowPromoted,
        HandDiscarded,
        TurnEnded,
        LevelChanged,
        LevelUpRewardsGenerated,
        CrawlerChanged,
        PlayerDamaged,
        RewardSelected,
        Log,
        PlayerRevived
    }

    /// <summary>
    /// 统一战斗事件记录。一场战斗中所有事件按顺序记录，支持未来回放和序列化。
    /// </summary>
    public struct CombatEventRecord
    {
        public CombatEventType Type;
        public int Turn;
        public int Sequence;
        public object Payload;

        public CombatEventRecord(CombatEventType type, int turn, int sequence, object payload)
        {
            Type = type;
            Turn = turn;
            Sequence = sequence;
            Payload = payload;
        }
    }

    /// <summary>
    /// 战斗事件流。每场战斗拥有独立实例，战斗结束时统一销毁。
    /// <para>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>统一订阅点：订阅方只需订阅一个 <see cref="OnEvent"/> 即可收到全部事件。</item>
    /// <item>顺序记录：所有事件按发生顺序记录在 <see cref="History"/> 中，支持回放。</item>
    /// <item>战斗隔离：战斗结束时调用 <see cref="Clear"/>，避免跨场景残留订阅。</item>
    /// <item>与 C# event 并行：现有 <c>CombatController</c> 的 <c>event Action&lt;T&gt;</c> 保留为向后兼容适配器。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class CombatEventStream
    {
        private readonly List<CombatEventRecord> _history = new List<CombatEventRecord>();
        private readonly List<Action<CombatEventRecord>> _subscribers = new List<Action<CombatEventRecord>>();
        private int _sequence = 0;

        /// <summary>当前回合数（由 CombatController 设置）。</summary>
        public int CurrentTurn { get; set; }

        /// <summary>事件历史记录（只读）。可用于回放和调试。</summary>
        public IReadOnlyList<CombatEventRecord> History => _history;

        /// <summary>订阅事件。返回 false 时流已 Clear。</summary>
        public void Subscribe(Action<CombatEventRecord> handler)
        {
            if (handler != null && !_subscribers.Contains(handler))
                _subscribers.Add(handler);
        }

        /// <summary>取消订阅。</summary>
        public void Unsubscribe(Action<CombatEventRecord> handler)
        {
            _subscribers.Remove(handler);
        }

        /// <summary>发布一个事件。</summary>
        public void Publish(CombatEventType type, object payload = null)
        {
            var record = new CombatEventRecord(type, CurrentTurn, _sequence++, payload);
            _history.Add(record);
            for (int i = 0; i < _subscribers.Count; i++)
                _subscribers[i].Invoke(record);
        }

        /// <summary>发布一个日志事件。</summary>
        public void PublishLog(string message)
        {
            Publish(CombatEventType.Log, message);
        }

        /// <summary>清空历史和订阅。战斗结束时调用。</summary>
        public void Clear()
        {
            _history.Clear();
            _subscribers.Clear();
            _sequence = 0;
        }

        /// <summary>历史记录数量。</summary>
        public int Count => _history.Count;

        /// <summary>是否有订阅者。</summary>
        public bool HasSubscribers => _subscribers.Count > 0;
    }
}
