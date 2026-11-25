using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 战斗阶段枚举。决定当前允许哪些操作。
    /// </summary>
    public enum BattlePhase
    {
        /// <summary>战斗初始化中（构造完成前）</summary>
        BattleInitializing,
        /// <summary>等待玩家输入（出牌/结束回合/抽牌）</summary>
        PlayerInput,
        /// <summary>卡牌结算中（禁止继续出牌）</summary>
        CardResolving,
        /// <summary>敌人已被击败，玩家可继续出牌积累余威；结束回合后结算胜利。</summary>
        Overkill,
        /// <summary>等待玩家选择升级奖励</summary>
        AwaitingReward,
        /// <summary>敌人回合开始</summary>
        EnemyTurnStarting,
        /// <summary>敌人行动中</summary>
        EnemyActing,
        /// <summary>战斗胜利</summary>
        BattleVictory,
        /// <summary>战斗失败</summary>
        BattleDefeat,
        /// <summary>战斗退出（场景切换中）</summary>
        BattleExiting
    }

    /// <summary>
    /// 战斗状态统一快照。是当前战斗数据的唯一真实来源。
    /// <para>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>外部只读，不能直接修改子系统内部状态。</item>
    /// <item>所有修改操作仍通过 <see cref="CombatController"/> 的方法。</item>
    /// <item>UI 显示的数据必须来自 <c>CombatState</c>，不能自行保存另一份权威状态。</item>
    /// <item><see cref="Random"/> 预留为可注入的随机源，当前未接入时为 null，调用方需回退到 UnityEngine.Random。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class CombatState
    {
        // ── 子系统引用（构造时赋值，运行时不变） ──

        /// <summary>法力系统</summary>
        public ManaSystem Mana { get; }

        /// <summary>连击系统</summary>
        public ComboSystem Combo { get; }

        /// <summary>牌区管理（牌库/手牌/弃牌堆）</summary>
        public DeckManager Deck { get; }

        /// <summary>玩家属性和状态</summary>
        public PlayerStats Player { get; }

        /// <summary>敌人列表（含网格位置）</summary>
        public IReadOnlyList<EnemyStats> Enemies { get; }

        /// <summary>Core 规则模块共享的同一敌人 roster；外部只能读取 Enemies。</summary>
        internal IList<EnemyStats> EnemyRoster { get; }

        /// <summary>Boss 劫眼计时器（可能为 null）</summary>
        public BossTimer BossTimer { get; }

        /// <summary>Boss 余威资格与活动尸体的唯一状态所有者。</summary>
        public BossOverkillSession BossOverkill { get; }

        /// <summary>英灵颜色触发系统</summary>
        public ColorTriggerSystem ColorTrigger { get; }

        /// <summary>状态效果系统</summary>
        public StatusSystem Statuses { get; internal set; }

        /// <summary>绑定战场格的地形效果系统</summary>
        public TerrainEffectSystem TerrainEffects { get; internal set; }

        /// <summary>本遭遇冻结攻击源与独立驻场鸟的唯一owner；只读来源供表现重建，恢复不重播历史。</summary>
        public PeriodicAttackSystem PeriodicAttacks { get; internal set; }

        /// <summary>本回合一次列补打队列的唯一 owner；不属于长期驻场来源。</summary>
        public ColumnReplayAttackSystem ColumnReplays { get; internal set; }

        /// <summary>本遭遇掠夺来源标记与一次性逐来源收益的唯一 owner。</summary>
        public MugMarkSystem MugMarks { get; internal set; }

        /// <summary>战斗事件流（每场战斗独立，战斗结束时销毁）</summary>
        public CombatEventStream Events { get; internal set; }

        /// <summary>表现队列（按序播放视觉表现，不影响战斗数据）</summary>
        public CombatPresentationQueue PresentationQueue { get; internal set; }

        /// <summary>敌人 AI 系统</summary>
        public EnemyAiSystem EnemyAi { get; internal set; }

        /// <summary>Read-only encounter interrupt admission and countdown observations.</summary>
        public EnemyInterruptRuntime EnemyInterrupts { get; internal set; }

        // ── 回合/等级状态 ──

        /// <summary>当前回合数（从 1 开始）</summary>
        public int CurrentTurn { get; internal set; }

        /// <summary>当前等级</summary>
        public int CurrentLevel { get; internal set; } = 1;

        /// <summary>当前经验值</summary>
        public int CurrentXP { get; internal set; }

        /// <summary>是否正在等待玩家选择升级奖励</summary>
        public bool AwaitingRewardSelection { get; internal set; }

        // ── 战斗阶段状态机 ──

        /// <summary>当前战斗阶段</summary>
        public BattlePhase CurrentPhase { get; internal set; } = BattlePhase.BattleInitializing;

        /// <summary>当前是否允许出牌</summary>
        public bool CanPlayCards => CurrentPhase == BattlePhase.PlayerInput || CurrentPhase == BattlePhase.Overkill;

        /// <summary>当前是否允许结束回合</summary>
        public bool CanEndTurn => CurrentPhase == BattlePhase.PlayerInput || CurrentPhase == BattlePhase.Overkill;

        /// <summary>当前是否允许抽牌</summary>
        public bool CanDrawCards => CurrentPhase == BattlePhase.PlayerInput;

        /// <summary>战斗是否已结束（胜利/失败/退出）</summary>
        public bool IsBattleOver => CurrentPhase == BattlePhase.BattleVictory
                                || CurrentPhase == BattlePhase.BattleDefeat
                                || CurrentPhase == BattlePhase.BattleExiting;

        // ── 随机源（预留，当前未接入） ──

        /// <summary>可注入的随机源。为 null 时调用方需回退到 UnityEngine.Random。</summary>
        public IRandomSource Random { get; set; }

        // ── 派生属性 ──

        /// <summary>当前手牌列表</summary>
        public IReadOnlyList<CardConfig> Hand => Deck.Hand;

        /// <summary>牌库列表</summary>
        public IReadOnlyList<CardConfig> DrawPile => Deck.Deck;

        /// <summary>弃牌堆列表</summary>
        public IReadOnlyList<CardConfig> DiscardPile => Deck.Discard;

        /// <summary>手牌数量</summary>
        public int HandCount => Deck.HandCount;

        /// <summary>牌库数量</summary>
        public int DeckCount => Deck.DeckCount;

        /// <summary>弃牌堆数量</summary>
        public int DiscardCount => Deck.DiscardCount;

        /// <summary>当前连击数（0=无连击）</summary>
        public int ComboCount => Combo.Combo;

        // ── 构造 ──

        internal CombatState(
            PlayerStats player,
            List<EnemyStats> enemies,
            DeckManager deck,
            ManaSystem mana,
            ComboSystem combo,
            ColorTriggerSystem colorTrigger,
            BossTimer bossTimer,
            BossOverkillSession bossOverkill)
        {
            Player = player;
            EnemyRoster = enemies;
            Enemies = enemies.AsReadOnly();
            Deck = deck;
            Mana = mana;
            Combo = combo;
            ColorTrigger = colorTrigger;
            BossTimer = bossTimer;
            BossOverkill = bossOverkill;
        }

        // ── 派生查询 ──

        /// <summary>战斗是否结束（阶段标记、全部敌人死亡/撤离或玩家死亡）</summary>
        public bool IsCombatOver()
        {
            if (IsBattleOver) return true;
            if (Player.currentHealth > 0 && HasOverkillTarget) return false;
            bool allResolved = true;
            for (int i = 0; i < Enemies.Count; i++)
            {
                EnemyStats enemy = Enemies[i];
                if (enemy != null && !enemy.IsDead && !enemy.IsWithdrawn)
                {
                    allResolved = false;
                    break;
                }
            }
            return allResolved || Player.currentHealth <= 0;
        }

        /// <summary>是否胜利（全部敌人死亡或撤离）</summary>
        public bool IsVictory
        {
            get
            {
                for (int i = 0; i < Enemies.Count; i++)
                {
                    EnemyStats enemy = Enemies[i];
                    if (enemy != null &&
                        !enemy.IsDead &&
                        !enemy.IsWithdrawn)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>是否失败（玩家死亡）</summary>
        public bool IsDefeat => Player.currentHealth <= 0;

        public bool HasOverkillTarget => BossOverkill?.IsActive == true;

        // ── 随机辅助 ──

        /// <summary>获取 0~1 的随机浮点数。优先用注入的 IRandomSource，回退到 UnityEngine.Random。</summary>
        public float RandomValue()
        {
            return Random != null ? Random.Value01() : UnityEngine.Random.value;
        }

        /// <summary>获取 [min, max) 范围的随机整数。优先用注入的 IRandomSource，回退到 UnityEngine.Random。</summary>
        public int RandomRange(int min, int max)
        {
            return Random != null ? Random.Range(min, max) : UnityEngine.Random.Range(min, max);
        }

        /// <summary>Float weighted choices share the injected random source; Unity's inclusive float range is retained.</summary>
        public float RandomFloatRange(float min, float max)
            => Random != null
                ? Random.Range(min, max)
                : UnityEngine.Random.Range(min, max);
    }
}
