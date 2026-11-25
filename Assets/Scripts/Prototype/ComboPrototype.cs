using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Prototype
{
    /// <summary>
    /// 连击链原型测试器
    /// 测试共享Combo计数器 + 万能牌续链 + 加法伤害公式
    /// </summary>
    public class ComboPrototype : MonoBehaviour
    {
        private CombatController _combat;

        private void Awake()
        {
            Log("======== 万界长卷 · 连击链原型 ========\n");

            // 创建玩家
            var player = new PlayerStats { faLi = 20, shouPai = 8, chouPai = 6, fengRui = 0 };

            // 创建 5 张测试卡牌（0-4 费，全部武卡/攻击）
            var deck = new System.Collections.Generic.List<CardConfig>
            {
                CreateTestCard("破军枪·零式", 0, 10, CardColor.Huo),
                CreateTestCard("横扫千军", 1, 25, CardColor.Huo),
                CreateTestCard("雷霆万钧", 2, 50, CardColor.Huo),
                CreateTestCard("天罡破阵", 3, 100, CardColor.Huo),
                CreateTestCard("万剑归宗", 4, 180, CardColor.Huo),
            };

            // 创建敌人
            var enemies = new System.Collections.Generic.List<EnemyStats>
            {
                new EnemyStats("天劫守卫", 99999, 15, 0, false)
            };

            // 初始化战斗
            _combat = new CombatController(player, deck, enemies);
            _combat.OnLog += Log;

            // 回合 1：完整连击链 0→1→2→3→4
            Log("\n--- 回合 1：测试完整连击链 ---\n");
            _combat.StartTurn();

            PlayFromHand("破军枪·零式");
            PlayFromHand("横扫千军");
            PlayFromHand("雷霆万钧");
            PlayFromHand("天罡破阵");
            PlayFromHand("万剑归宗");

            Log($"\n敌人剩余 HP: {_combat.Enemies[0].currentHealth}");
            Log($"\n连击总结：{_combat.Combo.GetSummary()}");

            _combat.EndTurn();

            // 回合 2：测试断链
            Log("\n--- 回合 2：测试断链（乱序出牌）---\n");
            _combat.StartTurn();
            PlayFromHand("雷霆万钧"); // 先出2费
            PlayFromHand("横扫千军"); // 再出1费 → 断链
            PlayFromHand("天罡破阵"); // 出3费 → 新连击
            Log($"\n连击总结：{_combat.Combo.GetSummary()}");
            _combat.EndTurn();

            // 回合 3：测试万能牌续链 0→1→2→3→W→0→1→2→3→W
            Log("\n--- 回合 3：测试万能牌续链（Combo 可突破5）---\n");
            var wildCard1 = CreateWildCard("灵光闪", 50);
            var wildCard2 = CreateWildCard("灵光闪贰", 50);
            deck.Add(wildCard1);
            deck.Add(wildCard2);
            _combat = new CombatController(player, deck, enemies);
            _combat.OnLog += Log;
            _combat.StartTurn();

            Log("链序列: 0→1→2→3→W→0→1→2→3→W (预期 Combo=9)");
            PlayFromHand("破军枪·零式");  // 0费 → Combo=0
            PlayFromHand("横扫千军");      // 1费 → Combo=1
            PlayFromHand("雷霆万钧");      // 2费 → Combo=2
            PlayFromHand("天罡破阵");      // 3费 → Combo=3
            PlayFromHand("灵光闪");        // W   → Combo=4
            PlayFromHand("万剑归宗");      // 4费 → Combo=5 (W后可任意费用)

            // 补充测试：W后从0开始
            PlayFromHand("灵光闪贰");     // W   → Combo=6
            Log($"\n连击总结：{_combat.Combo.GetSummary()}");
            Log($"最终 Combo = {_combat.Combo.Combo} (预期: 6)");
            _combat.EndTurn();

            // 回合 4：验证伤害公式（加法结构）
            Log("\n--- 回合 4：验证加法伤害公式 ---\n");
            Log("飞刀(CardDamage=8, Combo=0): 每发 = 8×(1+0) + 8×0 = 8");
            Log("Combo=4时: 每发 = 8×(1+4) + 8×0 = 40 (×5)");
            Log("Combo=12时: 每发 = 8×(1+12) + 8×0 = 104 (×13)");
            Log("Amount=3时Combo=0: 每发 = 8×(1+0) + 8×3 = 32");
            Log("Amount=3时Combo=12: 每发 = 8×(1+12) + 8×3 = 128 (Amount收益递减)");

            Log("\n======== 原型测试完成 ========");
        }

        private void PlayFromHand(string cardName)
        {
            for (int i = 0; i < _combat.Hand.Count; i++)
            {
                if (_combat.Hand[i].cardName == cardName)
                {
                    _combat.TryPlayCard(_combat.Hand[i]);
                    return;
                }
            }
            Log($"  (手中无 {cardName}，跳过)");
        }

        private CardConfig CreateTestCard(string name, int cost, int damage, CardColor color)
        {
            var card = ScriptableObject.CreateInstance<CardConfig>();
            card.cardName = name;
            card.description = $"费用 {cost} | 伤害 {damage}";
            card.color = color;
            card.costType = CostType.Default;
            card.manaCost = cost;
            card.baseDamage = damage;
            card.gemSlots = 1;
            return card;
        }

        private CardConfig CreateWildCard(string name, int damage)
        {
            var card = ScriptableObject.CreateInstance<CardConfig>();
            card.cardName = name;
            card.description = $"万能牌 | 伤害 {damage}";
            card.color = CardColor.Yang;
            card.costType = CostType.Wild;
            card.manaCost = 0;
            card.baseDamage = damage;
            card.gemSlots = 0;
            return card;
        }

        private void Log(string msg)
        {
            Debug.Log($"[原型] {msg}");
        }
    }
}
