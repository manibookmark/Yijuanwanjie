using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 条件判定输入。只读快照，传递给 ConditionEvaluator。
    /// 携带结算所需的所有上下文，不包含可变输出。
    /// </summary>
    public sealed class EffectResolveInput
    {
        /// <summary>玩家属性</summary>
        public PlayerStats player;

        /// <summary>法力系统</summary>
        public ManaSystem mana;

        /// <summary>敌人列表</summary>
        public IReadOnlyList<EnemyStats> enemies;

        /// <summary>玩家选择的目标索引</summary>
        public int targetIndex;

        /// <summary>当前连击计数（0=无连击）</summary>
        public int combo;

        /// <summary>本次出牌是否触发了连击</summary>
        public bool comboed;

        /// <summary>随机源（可注入，测试用确定性实现）</summary>
        public IRandomSource random;

        /// <summary>牌区管理器（用于抽牌、弃牌、生成卡牌效果）</summary>
        public DeckManager deck;

        /// <summary>当前打出的卡牌实例（用于自毁、费用修改效果）</summary>
        public CardInstance playedCard;

        /// <summary>按 definitionId 查找卡牌定义（用于 CreateCard 效果）</summary>
        public Func<string, CardDefinition> lookupDefinition;

        /// <summary>目标敌人是否为 Boss</summary>
        public bool TargetIsBoss
        {
            get
            {
                if (targetIndex < 0 || targetIndex >= enemies.Count) return false;
                return enemies[targetIndex].isBoss;
            }
        }

        /// <summary>目标敌人当前生命值比例（0~1）</summary>
        public float TargetHealthRatio
        {
            get
            {
                if (targetIndex < 0 || targetIndex >= enemies.Count) return 1f;
                var enemy = enemies[targetIndex];
                return enemy.baseHealth > 0
                    ? (float)enemy.currentHealth / enemy.baseHealth
                    : 0f;
            }
        }
    }

}
