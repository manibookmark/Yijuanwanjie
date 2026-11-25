using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace WanjieScroll.Core
{
    /// <summary>
    /// BOSS 在具体遭遇中的职责层级。None 表示只是沿用历史 isBoss 能力标签，
    /// 不触发清场入场；正式层 BOSS 使用 Floor，关 BOSS 使用 Stage。
    /// </summary>
    public enum BossRank
    {
        None = 0,
        Floor = 1,
        Stage = 2
    }

    /// <summary>
    /// 敌人配置
    /// </summary>
    [Serializable]
    public class EnemyStats
    {
        /// <summary>单场战斗内唯一且稳定的运行时身份；不得用 contentId 或列表索引替代。</summary>
        [field: NonSerialized]
        public int runtimeId { get; internal set; }
        [Tooltip("稳定内容标识；表现配置和存档关联不得使用显示名称")]
        public string contentId;
        public string name;
        [Tooltip("怪物 Hover 展示的一句身份描写；不得承载战斗规则")]
        public string shortDescription;
        public int baseHealth;
        public int currentHealth;
        public int maxHit;
        public int armor;
        [Min(0), Tooltip("护甲获得上限；0 表示沿用无上限的旧行为")]
        public int maxArmor;
        public bool isBoss;
        [Tooltip("遭遇职责；None=非关卡 BOSS，Floor=层 BOSS，Stage=关 BOSS。")]
        public BossRank bossRank = BossRank.None;
        [Min(0), Tooltip("击败后提供的基础经验；由内容配置直接提供")]
        public int experienceReward;

        [Header("控制抗性")]
        public EnemyBodySize bodySize = EnemyBodySize.Medium;
        [Tooltip("< 0 时按体型取默认值；Boss 默认不会高于 0.25")]
        public float knockbackMultiplier = -1f;
        public bool canBeDisplaced = true;
        [Tooltip("< 0 时普通敌人为 1，Boss 为 0.5")]
        public float disarmMultiplier = -1f;

        [NonSerialized] public bool isDisarmed;
        /// <summary>
        /// BossOverkillSession 的只读兼容投影；Core 之外不得写入。
        /// </summary>
        public bool isOverkillTarget { get; internal set; }
        [NonSerialized] public bool justFilledFrontRow;

        /// <summary>
        /// 是否已经部署到战场。关 BOSS 待命时保持 false，但仍是存活敌人，
        /// 因而不会被选中、行动或参与阵型移动，也不会导致战斗提前胜利。
        /// 待命部署由 <see cref="LevelBossEncounter"/> 独占；普通敌人的永久
        /// 撤离只能通过 <see cref="TryWithdraw"/> 完成。
        /// </summary>
        public bool IsDeployed { get; internal set; } = true;

        /// <summary>
        /// 是否已主动撤离本场战斗。撤离者仍保持原生命值，且不会进入死亡、
        /// 击败、击杀或经验奖励链。
        /// </summary>
        public bool IsWithdrawn { get; private set; }

        /// <summary>内容层注入的固定行为定义；未配置时沿用旧随机 AI。</summary>
        [NonSerialized] public EnemyBehaviorDefinition behaviorDefinition;

        /// <summary>固定行为序列的下一项游标；由 EnemyAiSystem 独占推进。</summary>
        public int behaviorActionIndex;

        /// <summary>
        /// 旧行为的不可逆组状态（0/1）；健康分组模式下仅为选择器当前组的
        /// 读投影（可回退、可超过1）。由Core写入，表现层不推断或推进。
        /// </summary>
        public int behaviorPhaseIndex;

        /// <summary>
        /// 已部署后真正结算的合法自身行动数；Stage 待命期间不会增长。
        /// </summary>
        public int behaviorLegalActionCount;

        /// <summary>分波索引。0=第一波（初始可见），1+=后续波次（初始隐藏，前置波全灭后入场）。</summary>
        [NonSerialized] public int spawnWaveIndex;

        /// <summary>占格起始排：0=前排；完整纵深进入前三排才入场。普通补位每步一排，空场恢复可连续合法推进。</summary>
        public int row;

        /// <summary>阵法中的起始列，由 EnemyFormationLayout 统一分配。</summary>
        public int column = -1;

        /// <summary>
        /// 保留作者源矩阵的原始行索引。-1 表示当前遭遇使用的是自动、分波、
        /// 按行或旧图案布阵，不能把运行时 row 误读成源证据。
        /// </summary>
        [NonSerialized] public int sourceFormationRow = -1;

        /// <summary>保留作者源矩阵的原始列索引；未配置显式源矩阵时为 -1。</summary>
        [NonSerialized] public int sourceFormationColumn = -1;

        /// <summary>
        /// 作者源矩阵的列数。该值与当前项目战场的固定列数分离，以免把
        /// 4 列来源矩阵无声压扁为 5 列表现图案。
        /// </summary>
        [NonSerialized] public int sourceFormationColumnCount;

        [Range(1, EnemyFormationLayout.Columns), Tooltip("横向占用格数")]
        public int gridWidth = 1;

        [Range(1, EnemyFormationLayout.VisibleRows), Tooltip("从所在排向后纵向占用的格数")]
        public int gridDepth = 1;

        [Tooltip("敌人立绘/图标")]
        public Sprite enemyArt;

        public EnemyStats(
            string name,
            int hp,
            int maxHit = 0,
            int armor = 0,
            bool isBoss = false,
            string contentId = null,
            int gridWidth = 1,
            int gridDepth = 1,
            EnemyBodySize bodySize = EnemyBodySize.Medium,
            float knockbackMultiplier = -1f,
            bool canBeDisplaced = true,
            float disarmMultiplier = -1f,
            int experienceReward = 0,
            int maxArmor = 0,
            EnemyBehaviorDefinition behaviorDefinition = null,
            string shortDescription = null,
            BossRank bossRank = BossRank.None)
        {
            this.contentId = contentId;
            this.name = name;
            this.shortDescription = shortDescription;
            this.baseHealth = hp;
            this.currentHealth = hp;
            this.maxHit = maxHit;
            this.armor = armor;
            this.maxArmor = Math.Max(0, maxArmor);
            this.isBoss = isBoss;
            this.bossRank = bossRank;
            this.experienceReward = Math.Max(0, experienceReward);
            this.bodySize = bodySize;
            this.knockbackMultiplier = knockbackMultiplier;
            this.canBeDisplaced = canBeDisplaced;
            this.disarmMultiplier = disarmMultiplier;
            this.behaviorDefinition = behaviorDefinition;
            this.behaviorActionIndex = 0;
            EnemyFormationLayout.EnsureValidFootprint(
                gridWidth,
                gridDepth,
                $"敌人 {contentId ?? name}");
            this.gridWidth = gridWidth;
            this.gridDepth = gridDepth;
        }

        public int GridWidth => gridWidth;
        public int GridDepth => gridDepth;

        public bool IsDead => currentHealth <= 0;

        public float EffectiveKnockbackMultiplier
        {
            get
            {
                float value = knockbackMultiplier >= 0f
                    ? knockbackMultiplier
                    : bodySize switch
                    {
                        EnemyBodySize.Small => 1.25f,
                        EnemyBodySize.Medium => 1f,
                        EnemyBodySize.Large => 0.5f,
                        _ => 0.25f
                    };
                return isBoss ? Math.Min(0.25f, value) : value;
            }
        }

        public float EffectiveDisarmMultiplier
            => disarmMultiplier >= 0f ? disarmMultiplier : (isBoss ? 0.5f : 1f);

        /// <summary>
        /// 把一名仍在场且存活的敌人永久移出本场战斗。规则协调器是唯一调用者；
        /// 返回 false 表示目标已死亡、已撤离或尚未部署。
        /// </summary>
        internal bool TryWithdraw()
        {
            if (IsDead || IsWithdrawn || !IsDeployed)
                return false;

            IsWithdrawn = true;
            IsDeployed = false;
            isDisarmed = false;
            justFilledFrontRow = false;
            return true;
        }

        /// <summary>
        /// Removes an unknown actor from an exclusively owned hypothetical
        /// battle without a defeat, experience award or formation event.
        /// Actual gameplay withdrawal continues to use TryWithdraw.
        /// </summary>
        internal void ExcludeFromHypothesis()
        {
            IsWithdrawn = true;
            IsDeployed = false;
            isOverkillTarget = false;
            isDisarmed = false;
            justFilledFrontRow = false;
        }

        /// <summary>
        /// 创建规则仿真专用深快照。预览不得在各解析器中手抄字段，否则新增
        /// 状态（例如撤离）时会悄然造成 Preview/Actual 分歧。
        /// Boss 余威兼容投影不在此复制，由克隆侧 BossOverkillSession 重建。
        /// </summary>
        internal EnemyStats CloneForSimulation()
        {
            return new EnemyStats(
                name,
                baseHealth,
                maxHit,
                armor,
                isBoss,
                contentId,
                gridWidth,
                gridDepth,
                bodySize,
                knockbackMultiplier,
                canBeDisplaced,
                disarmMultiplier,
                experienceReward,
                maxArmor,
                behaviorDefinition,
                shortDescription,
                bossRank)
            {
                runtimeId = runtimeId,
                currentHealth = currentHealth,
                row = row,
                column = column,
                isDisarmed = isDisarmed,
                justFilledFrontRow = justFilledFrontRow,
                behaviorActionIndex = behaviorActionIndex,
                behaviorPhaseIndex = behaviorPhaseIndex,
                behaviorLegalActionCount = behaviorLegalActionCount,
                spawnWaveIndex = spawnWaveIndex,
                sourceFormationRow = sourceFormationRow,
                sourceFormationColumn = sourceFormationColumn,
                sourceFormationColumnCount = sourceFormationColumnCount,
                IsDeployed = IsDeployed,
                IsWithdrawn = IsWithdrawn,
                enemyArt = enemyArt
            };
        }
    }

    /// <summary>
    /// 玩家属性表
    /// </summary>
    [Serializable]
    public class PlayerStats
    {
        [Header("基础属性")]
        public int maxHealth = 100;
        public int currentHealth = 100;
        public int armor = 0;
        [Min(0), Tooltip("每个玩家回合开始时获得的基础护甲")]
        public int baseArmorPerTurn = 0;

        [Header("Deck Stats")]
        public int fengRui = 0;      // 锋锐（Might）
        [FormerlySerializedAs("fenGuang")]
        public int amount = 0;      // 数量（Amount）- 卡牌攻击物数加值
        public int bengLie = 0;     // 裂空（Area）
        public int ningShen = 0;      // 凝神入口加值；运行时由单次储备/驻場英灵拥有，不重复随召唤追加
        public int shouPai = 5;     // 展卷（历史字段名）- 每回合标准抽牌数，非持有上限
        public int yinJuan = 0;     // 引卷（Magnet）- 显式抽牌加值
        // 旧场景/存档兼容字段；不得再作为每回合标准补牌作者源。
        public int chouPai = 1;
        public int tianYun = 0;      // 天运（Luck）
        public int lianYu = 0;     // 敛玉（Greed）
        public int mingWu = 0;      // 明悟（Growth）
        public int hunHun = 0;      // 还魂（Revival）
        public int faLi = 3;        // 灵力（Mana）- 法力上限
        public int zhaoSha = 0;     // 招煞（Curse）
        public int recovery = 0;   // 调息（Recovery）- 每战后回血

        /// <summary>
        /// 属性乘区 = 属性值 / 100 + 1.0
        /// </summary>
        public static float GetMultiplier(int statValue)
        {
            return statValue / 100f + 1.0f;
        }

        /// <summary>
        /// 兼容入口：只计算 Direct Hit。新攻击必须使用 AttackResolver 计算 Hit 数与 Area。
        /// </summary>
        /// <param name="cardDamage">卡牌每发伤害（CardDamage = BaseDamage）</param>
        /// <param name="combo">当前连击计数（0=无连击）</param>
        /// <returns>单发伤害（不含暴击/灵印，由调用方后续乘）</returns>
        public int CalculateDamage(int cardDamage, int combo)
        {
            return DamageSystem.RoundHalfUp(cardDamage * GetMultiplier(fengRui));
        }

        /// <summary>
        /// 计算获得经验
        /// 基础经验 × (悟性 / 100 + 1.0)
        /// </summary>
        public int CalculateXP(int baseXP)
        {
            return Mathf.FloorToInt(baseXP * GetMultiplier(mingWu) + 0.5f);
        }

        /// <summary>
        /// 提高最大生命并保持当前生命的绝对值不变。
        /// 返回实际增加量；非正数请求不产生变化。
        /// </summary>
        public int IncreaseMaxHealth(int amount)
        {
            int increase = Math.Max(0, amount);
            maxHealth += increase;
            return increase;
        }
    }

    /// <summary>
    /// 升级所需经验
    /// 25 × level²
    /// </summary>
    public static class LevelSystem
    {
        public static int GetRequiredXP(int level)
        {
            return 25 * level * level;
        }

        /// <summary>
        /// 基础经验系数（可调）
        /// </summary>
        public const int BaseXpReq = 5;
        public const float XpCurveMultiplier = 5.0f;
        public const float XpCurveExponent = 2.0f;
    }
}
