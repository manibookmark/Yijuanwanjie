using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Prototype
{
    /// <summary>
    /// Phase 1 垂直切片测试器（修正版）
    /// 修正：牌库精简，确保测试卡牌全部上手
    /// </summary>
    public class Phase1Prototype : MonoBehaviour
    {
        private CombatController _combat;

        private string _logPath;
        private System.Text.StringBuilder _logBuilder = new System.Text.StringBuilder();

        /// <summary>
        /// Batch mode 入口（命令行 -executeMethod 调用）
        /// </summary>
        public static void Run()
        {
            var go = new GameObject("Phase1Test");
            var comp = go.AddComponent<Phase1Prototype>();
            // Awake 会在 AddComponent 时自动调用
        }

        private void Awake()
        {
            _logPath = System.IO.Path.Combine(Application.temporaryCachePath, "phase1_log.txt");
            _logBuilder.Clear();
            Log("======== 万界长卷 · Phase 1 垂直切片 ========\n");

            // 创建玩家：手牌上限=8，抽牌=8，确保全部上手
            var player = new PlayerStats
            {
                maxHealth = 120,
                currentHealth = 120,
                faLi = 12,
                shouPai = 8,
                chouPai = 8,
                fengRui = 0
            };

            // 精简牌库：只放测试需要的卡，确保全部抽到手中
            var deck = new List<CardConfig>
            {
                // 武卡（红）0-4 费连击链
                CreateCard("破军枪", 0, 10, CardColor.Huo),
                CreateCard("横扫千军", 1, 25, CardColor.Huo),
                CreateCard("雷霆万钧", 2, 50, CardColor.Huo),
                CreateCard("天罡破阵", 3, 100, CardColor.Huo),
                CreateCard("万剑归宗", 4, 180, CardColor.Huo),
                // 御卡（蓝）
                CreateArmorCard("铁壁", 0, 4),
                // 灵卡（紫）
                CreateManaCard("聚灵诀", 0, 2),
            };

            // 升级奖池
            var upgradePool = new List<CardConfig>
            {
                CreateCard("碎星锤", 2, 60, CardColor.Huo),
                CreateArmorCard("玄铁盾", 1, 4),
            };

            // 创建敌人：弱化妖兵以便击杀触发升级
            var enemies = new List<EnemyStats>
            {
                new EnemyStats("妖兵", 80, 5, 0, false),     // 80HP，一击可杀
                new EnemyStats("天劫守卫", 1500, 20, 10, true),
            };

            // Boss 计时器（5 眼，25 伤害）
            var bossTimer = new BossTimer(5, 25);

            // 初始化战斗
            _combat = new CombatController(player, deck, enemies, bossTimer, upgradePool);
            _combat.OnLog += Log;
            _combat.OnLevelUp += level => Log($"  → Lv{level}！选择升级奖励中...");

            // 创建英灵
            var crawler = ScriptableObject.CreateInstance<CrawlerConfig>();
            crawler.crawlerName = "哪吒";
            crawler.description = "红卡触发：伤害+10%";
            crawler.triggerColor = CardColor.Huo;
            crawler.triggerEffect = "武威+10%";
            crawler.duration = 5;
            crawler.cardMana = 0;
            crawler.enterEffect = "三头六臂，火焰枪法";

            Log("\n--- 召唤英灵 ---\n");
            _combat.SummonCrawler(crawler);

            // === 回合 1：完整连击链 + 击杀妖兵升级 ===
            Log("\n--- 回合 1：完整连击链 0→1→2→3→4 + 击杀妖兵 ---\n");
            _combat.StartTurn();

            // 先杀妖兵（target=0）触发升级
            PlayFromHand("破军枪", 0);       // 0费 → 妖兵 -10HP (连击×1)
            PlayFromHand("聚灵诀", 0);       // 0费灵卡 → 补法力+2，连击继续
            PlayFromHand("横扫千军", 0);      // 1费 → 妖兵 -50HP (连击×2)
            PlayFromHand("雷霆万钧", 0);      // 2费 → 妖兵 -150HP (连击×3) → 妖兵死(80HP)

            Log($"\n妖兵应已死亡，升级应触发");
            Log($"敌人状态: 妖兵 HP={_combat.Enemies[0].currentHealth} | 天劫守卫 HP={_combat.Enemies[1].currentHealth}");
            Log($"等级: Lv{_combat.CurrentLevel} XP={_combat.CurrentXP}/{LevelSystem.GetRequiredXP(_combat.CurrentLevel)}");
            Log($"玩家: HP={player.currentHealth} 护甲={player.armor} {_combat.Mana}");
            Log($"天劫: {_combat.BossTimer.GetSummary()}");

            // 继续打 Boss
            PlayFromHand("天罡破阵", 1);      // 3费 → Boss (连击×4)
            PlayFromHand("万剑归宗", 1);      // 4费 → Boss (连击×5)

            Log($"\n连击总结: {_combat.Combo.GetSummary()}");
            Log($"敌人状态: 天劫守卫 HP={_combat.Enemies[1].currentHealth}");
            Log($"天劫: {_combat.BossTimer.GetSummary()}");

            _combat.EndTurn();

            // === 回合 2：防御 + Boss 天劫 ===
            Log("\n--- 回合 2：防御 + Boss 天劫 ---\n");
            _combat.StartTurn();

            PlayFromHand("铁壁", 0);  // +护甲

            Log($"\n玩家护甲: {player.armor}，结束回合让天劫触发...");
            _combat.EndTurn();

            Log($"\n天劫后: HP={player.currentHealth} 护甲={player.armor}");

            // === 回合 3：继续输出 Boss ===
            Log("\n--- 回合 3：继续输出 ---\n");
            _combat.StartTurn();

            // 剩余卡牌打出
            PlayFromHand("破军枪", 1);
            PlayFromHand("横扫千军", 1);
            PlayFromHand("雷霆万钧", 1);
            PlayFromHand("天罡破阵", 1);
            PlayFromHand("万剑归宗", 1);

            Log($"\n敌人: 天劫守卫 HP={_combat.Enemies[1].currentHealth}");
            Log($"等级: Lv{_combat.CurrentLevel} | 武威={player.fengRui} 数量={player.amount}");
            _combat.EndTurn();

            // === 战斗结果 ===
            Log("\n======== 战斗结果 ========");
            Log($"胜利: {_combat.IsVictory} | 战斗结束: {_combat.IsCombatOver()}");
            Log($"玩家: HP={player.currentHealth}/{player.maxHealth} 护甲={player.armor}");
            Log($"等级: Lv{_combat.CurrentLevel} | 武威={player.fengRui} 数量={player.amount}");
            Log($"天劫释放次数判定: HP从120降到{player.currentHealth}");
            Log("\n======== Phase 1 测试完成 ========");

            // 写入日志文件
            System.IO.File.WriteAllText(_logPath, _logBuilder.ToString());
            Debug.Log($"[Phase1] 日志已保存: {_logPath}");
        }

        private void PlayFromHand(string cardName, int targetIndex)
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

        private CardConfig CreateCard(string name, int cost, int damage, CardColor color)
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

        private CardConfig CreateArmorCard(string name, int cost, int armor)
        {
            var card = ScriptableObject.CreateInstance<CardConfig>();
            card.cardName = name;
            card.description = $"费用 {cost} | +{armor}护甲";
            card.color = CardColor.Tu;
            card.costType = CostType.Default;
            card.manaCost = cost;
            card.baseDamage = 0;
            card.armor = armor;
            card.gemSlots = 1;
            return card;
        }

        private CardConfig CreateManaCard(string name, int cost, int manaGain)
        {
            var card = ScriptableObject.CreateInstance<CardConfig>();
            card.cardName = name;
            card.description = $"费用 {cost} | +{manaGain}法力";
            card.color = CardColor.Shui;
            card.costType = CostType.Default;
            card.manaCost = cost;
            card.baseDamage = 0;
            card.manaGain = manaGain;
            card.gemSlots = 1;
            return card;
        }

        private void Log(string msg)
        {
            string line = $"[Phase1] {msg}";
            Debug.Log(line);
            _logBuilder.AppendLine(line);
        }
    }
}
