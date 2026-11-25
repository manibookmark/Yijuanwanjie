using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.UI
{
    /// <summary>
    /// UI 展示数据结构（纯数据，不依赖 UnityEngine，支持 headless 测试）
    /// </summary>

    /// <summary>
    /// 卡牌展示数据
    /// </summary>
    public struct CardDisplayData
    {
        public int runtimeInstanceId;
        public int limitBreakCount;
        public string contentId;
        public string name;
        public string description;
        public string detailDescription;
        public CardColor color;
        public int cost;
        public CostType costType;
        public bool hasConfusedCostChange; // Core-owned current numeric change; zero rolls stay unmarked.
        public int damage;
        public int baseManaCost; // 灵力增伤读取作者基础费用，不使用免费/减费后的费用
        public long manaBeforePayment; // 本次手牌快照的当前灵力，由战斗唯一owner提供
        public int armor;
        public int heal;
        public int manaGain;
        public StatusEffect statusEffect;
        public float statusChance;
        public bool autoPlayOnDraw;
        public bool destroyOnUse;
        public bool isEncounterTemporary; // 来自实体生命周期，与打出后销毁独立
        public AttackPattern attackPattern;
        public int projectileCount;
        public int might;  // 锋锐百分比，用于动态计算卡面伤害
        public int amount; // 数量属性，用于计算卡牌最终攻击物数
        public List<CardEffectSpec> effects;
        public bool canEvolve;
        public int useCount;
        public int shatterThreshold;
        public bool isShattered;
        public bool canPlay;
        public bool doesCombo;
        public bool isFreeToPlay;
        public int sealSlotCapacity;
        public List<CardSealSpec> seals;
        public float previewDamageMultiplier;
        // Detached host attack structure for explaining ordinary per-hit values.
        // Reward/pile snapshots use their base context and have no immediate total.
        public CardAttackDisplaySummary attackDisplay;
        // The same frozen Core result owns the hand total and enemy hover rows.
        // UI must not re-sum targets or independently roll preview decisions.
        public CardImmediateDamageDisplaySummary immediateDamageDisplay;
        public int displayCombo;
        public bool usesCurrentPlayDisplay;

        public bool IsShattered => false;

        /// <summary>
        /// 从 CardConfig 构建基础展示；战斗手牌另注入当前出牌摘要。
        /// </summary>
        public static CardDisplayData FromCard(CardConfig card, int useCount = 0,
            int shatterThreshold = 0, bool canPlay = true, int might = 0,
            int amount = 0, long manaBeforePayment = 0)
        {
            return new CardDisplayData
            {
                runtimeInstanceId = card.runtimeInstanceId,
                limitBreakCount = card.LimitBreakCount,
                contentId = card.contentId,
                name = card.cardName,
                description = card.description,
                detailDescription = card.detailDescription,
                color = card.color,
                cost = card.GetCost(),
                costType = card.costType,
                hasConfusedCostChange = card.HasConfusedCostChange,
                damage = card.baseDamage,
                baseManaCost = card.manaCost,
                manaBeforePayment = System.Math.Max(0L, manaBeforePayment),
                armor = card.armor,
                heal = card.heal,
                manaGain = card.manaGain,
                statusEffect = card.statusEffect,
                statusChance = card.statusChance,
                autoPlayOnDraw = card.autoPlayOnDraw,
                destroyOnUse = card.MustDestroyOnUse,
                isEncounterTemporary = card.IsEncounterTemporary,
                attackPattern = card.attackPattern,
                projectileCount = card.projectileCount,
                might = might,
                amount = amount,
                effects = card.effects,
                canEvolve = card.evolutionTarget != null,
                useCount = 0,
                shatterThreshold = 0,
                isShattered = false,
                canPlay = canPlay,
                doesCombo = card.DoesCardCombo(),
                isFreeToPlay = card.isFreeToPlay,
                sealSlotCapacity = CardSealRules.GetSlotCapacity(card),
                seals = CloneSeals(card.seals),
                previewDamageMultiplier = 1f,
                attackDisplay = CardAttackDisplayProjection.Project(card,
                    new CombatStatSnapshot(new PlayerStats
                    {
                        fengRui = System.Math.Max(0, might),
                        amount = System.Math.Max(0, amount)
                    }, 0, manaBeforePayment: manaBeforePayment)),
                displayCombo = 0,
                usesCurrentPlayDisplay = false
            };
        }

        private static List<CardSealSpec> CloneSeals(
            IReadOnlyList<CardSealSpec> source)
        {
            var result = new List<CardSealSpec>();
            if (source == null)
                return result;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                    result.Add(source[i].Clone());
            }
            return result;
        }
    }

    /// <summary>
    /// 连击链条目
    /// </summary>
    public struct ChainEntry
    {
        public int position;       // 在链中的位置（0-based）
        public int cost;           // 卡牌费用
        public int multiplier;    // 该位置的乘区
        public bool isCombo;       // 是否成功连击
    }

    /// <summary>
    /// 敌人展示数据
    /// </summary>
    public struct EnemyDisplayData
    {
        public string name;
        public int currentHP;
        public int baseHP;
        public int maxHit;
        public int armor;
        public bool isBoss;
        public bool isDead;

        public float HPRatio => baseHP > 0 ? (float)currentHP / baseHP : 0f;

        public static EnemyDisplayData FromEnemy(EnemyStats enemy)
        {
            return new EnemyDisplayData
            {
                name = enemy.name,
                currentHP = enemy.currentHealth,
                baseHP = enemy.baseHealth,
                maxHit = enemy.maxHit,
                armor = enemy.armor,
                isBoss = enemy.isBoss,
                isDead = enemy.IsDead
            };
        }
    }

    /// <summary>
    /// 英灵展示数据
    /// </summary>
    public struct CrawlerDisplayData
    {
        public string name;
        public CardColor triggerColor;
        public int remainingTriggers;
        public int duration;

        public static CrawlerDisplayData FromCrawler(ColorTriggerSystem.ActiveCrawler ac)
        {
            return new CrawlerDisplayData
            {
                name = ac.config.crawlerName,
                triggerColor = ac.EffectiveTriggerColor,
                remainingTriggers = ac.remainingTriggers,
                duration = ac.config.duration
            };
        }
    }

    /// <summary>
    /// 升级奖励展示数据
    /// </summary>
    public struct RewardDisplayData
    {
        public LevelUpSystem.RewardType type;
        public string name;
        public string description;
        public StatType statType;
        public int statAmount;
        public bool canEvolve;      // 如果是新卡且可进化
        public string evolutionHint; // 进化目标提示

        public static RewardDisplayData FromReward(LevelUpSystem.Reward reward)
        {
            var data = new RewardDisplayData
            {
                type = reward.type,
                name = reward.name,
                description = reward.card != null
                    ? Cards.CardTextRenderer.GetDisplayDescription(CardDisplayData.FromCard(reward.card))
                    : reward.description,
                statType = reward.statType,
                statAmount = reward.statAmount,
                canEvolve = reward.card != null && reward.card.evolutionTarget != null,
                evolutionHint = reward.card != null && reward.card.evolutionTarget != null
                    ? $"→ 可进化为「{reward.card.evolutionTarget.cardName}」"
                    : null
            };
            return data;
        }
    }

    /// <summary>
    /// 房间展示数据
    /// </summary>
    public struct RoomDisplayData
    {
        public int id;
        public int x;
        public int y;
        public Dungeon.RoomType type;
        public string typeName;
        public bool visited;
        public bool cleared;
        public bool isCurrent;
        public bool isAccessible;
        public List<int> connections;

        public static RoomDisplayData FromRoom(Dungeon.Room room, bool isCurrent, bool isAccessible)
        {
            return new RoomDisplayData
            {
                id = room.id,
                x = room.x,
                y = room.y,
                type = room.type,
                typeName = ExplorationController.GetRoomTypeName(room.type),
                visited = room.visited,
                cleared = room.cleared,
                isCurrent = isCurrent,
                isAccessible = isAccessible,
                connections = room.connections
            };
        }
    }
}
