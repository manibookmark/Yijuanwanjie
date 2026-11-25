using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 战斗指令接口。UI 层创建指令，CombatController 验证并执行。
    /// <para>指令表示"请求做某件事"，与事件（表示"已经发生"）区分。</para>
    /// </summary>
    public interface ICombatCommand
    {
        /// <summary>指令类型名称，用于日志和调试。</summary>
        string CommandType { get; }
    }

    /// <summary>
    /// 指令执行结果。
    /// </summary>
    public struct CommandResult
    {
        public bool Success { get; set; }
        public CommandError Error { get; set; }
        public string Message { get; set; }

        public static CommandResult Ok => new() { Success = true };
        public static CommandResult Fail(CommandError error, string message = null) =>
            new() { Success = false, Error = error, Message = message };

        public override string ToString() =>
            Success ? "OK" : $"{Error}: {Message}";
    }

    /// <summary>
    /// 指令错误类型。
    /// </summary>
    public enum CommandError
    {
        None,
        InvalidPhase,       // 当前阶段不允许此操作
        InsufficientMana,   // 法力不足
        CardNotInHand,      // 卡牌不在手牌中
        InvalidTarget,      // 目标无效
        NoCardsToDraw,      // 牌库和弃牌堆都空了
        CombatOver,         // 战斗已结束
        NoRewardPending,    // 没有待选择的升级奖励
        InvalidIndex         // 索引越界
    }

    // ──────────────────────────────────────────────
    //  具体指令
    // ──────────────────────────────────────────────

    /// <summary>出牌指令。</summary>
    public struct PlayCardCommand : ICombatCommand
    {
        public string CommandType => "PlayCard";
        public CardConfig Card { get; set; }
        public int TargetIndex { get; set; }

        public PlayCardCommand(CardConfig card, int targetIndex = 0)
        {
            Card = card;
            TargetIndex = targetIndex;
        }
    }

    /// <summary>结束玩家回合，结算敌方行动并在存活时开始下一回合。</summary>
    public struct EndTurnCommand : ICombatCommand
    {
        public string CommandType => "EndTurn";
    }

    /// <summary>抽牌指令。</summary>
    public struct DrawCardsCommand : ICombatCommand
    {
        public string CommandType => "DrawCards";
        public int Count { get; set; }

        public DrawCardsCommand(int count = 1)
        {
            Count = count;
        }
    }

    /// <summary>选择升级奖励指令。</summary>
    public struct SelectRewardCommand : ICombatCommand
    {
        public string CommandType => "SelectReward";
        public int RewardIndex { get; set; }

        public SelectRewardCommand(int rewardIndex)
        {
            RewardIndex = rewardIndex;
        }
    }

    /// <summary>弃掉手牌指令。</summary>
    public struct DiscardHandCommand : ICombatCommand
    {
        public string CommandType => "DiscardHand";
    }
}
