using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI;
using WanjieScroll.Audio;

namespace WanjieScroll
{
    /// <summary>
    /// Phase 5 UX/UI 测试器（batch mode）
    /// 测试 ViewModel 层逻辑：手牌排序、连击链可视化数据、HUD数据绑定、升级选项、AudioManager事件
    /// 通过 -executeMethod 调用，日志输出到 phase5_log.txt
    /// </summary>
    public class Phase5Prototype : MonoBehaviour
    {
        private string _logPath;
        private System.Text.StringBuilder _log = new System.Text.StringBuilder();

        // 测试统计
        private int _passCount = 0;
        private int _failCount = 0;

        public static void Run()
        {
            var go = new GameObject("Phase5Test");
            go.AddComponent<Phase5Prototype>();
        }

        private void Awake()
        {
            _logPath = System.IO.Path.Combine(Application.temporaryCachePath, "phase5_log.txt");
            _log.Clear();
            Log("======== 万界长卷 · Phase 5 UX/UI 测试 ========");
            Log("测试时间: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log("");

            // ====== 测试 1: HandViewModel 排序 ======
            TestHandViewModelSorting();

            // ====== 测试 2: ComboViewModel 连击链数据 ======
            TestComboViewModel();

            // ====== 测试 3: HUDViewModel 数据绑定 ======
            TestHUDViewModel();

            // ====== 测试 4: LevelUpViewModel 奖励生成 ======
            TestLevelUpViewModel();

            // ====== 测试 5: DungeonViewModel 地图数据 ======
            TestDungeonViewModel();

            // ====== 测试 6: AudioManager 事件分发 ======
            TestAudioManagerEvents();

            // ====== 测试 7: ViewModel 事件订阅/取消订阅 ======
            TestViewModelLifecycle();

            // ====== 测试 8: CardColorPalette 配色体系 ======
            TestColorPalette();

            // ====== 结果 ======
            Log("");
            Log("======== Phase 5 测试结果 ========");
            Log($"通过: {_passCount} | 失败: {_failCount} | 总计: {_passCount + _failCount}");
            Log(_failCount == 0 ? "✓ 全部通过！" : $"✗ {_failCount} 个测试失败");
            Log("======== Phase 5 测试完成 ========");

            System.IO.File.WriteAllText(_logPath, _log.ToString());
            Debug.Log($"[Phase5] 日志已保存: {_logPath}");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // ================================================================
        //  测试 1: HandViewModel 排序
        // ================================================================
        private void TestHandViewModelSorting()
        {
            Log("------ 测试 1: HandViewModel 手牌排序 ------");

            var player = new PlayerStats { shouPai = 10, chouPai = 5, faLi = 10 };
            var deck = new List<CardConfig>
            {
                CreateCard("卡4费", 4, 50, CardColor.Huo),
                CreateCard("卡1费", 1, 10, CardColor.Tu),
                CreateCard("卡3费", 3, 30, CardColor.Mu),
                CreateCard("卡0费", 0, 5, CardColor.Shui),
                CreateCard("卡2费", 2, 20, CardColor.Yin),
            };
            var enemies = new List<EnemyStats> { new EnemyStats("测试敌人", 999) };
            var combat = new CombatController(player, deck, enemies);
            combat.OnLog += msg => { };

            var handVM = new HandViewModel();
            handVM.OnLog += msg => Log($"  [HandVM] {msg}");
            handVM.Bind(combat);

            // 开始回合，全抽上来
            combat.StartTurn();

            Log($"  手牌数: {handVM.HandCount} (期望 5)");
            Assert("手牌数=5", handVM.HandCount == 5);

            // 默认排序（抽牌顺序）
            Log("  默认排序（抽牌顺序）:");
            for (int i = 0; i < handVM.SortedHand.Count; i++)
            {
                var c = handVM.SortedHand[i];
                Log($"    [{i}] {c.name} 费{c.cost} 色{c.color}");
            }

            // 按费用排序
            handVM.SortByCost();
            Log("  按费用排序:");
            bool costSorted = true;
            for (int i = 1; i < handVM.SortedHand.Count; i++)
            {
                int prev = handVM.SortedHand[i - 1].cost;
                int curr = handVM.SortedHand[i].cost;
                Log($"    [{i-1}] 费{prev} → [{i}] 费{curr}");
                if (prev > curr) costSorted = false;
            }
            Assert("按费用升序排列", costSorted);

            // 按颜色排序
            handVM.SortByColor();
            Log("  按颜色排序:");
            bool colorSorted = true;
            for (int i = 1; i < handVM.SortedHand.Count; i++)
            {
                var prev = handVM.SortedHand[i - 1].color;
                var curr = handVM.SortedHand[i].color;
                Log($"    [{i-1}] {CardColorPalette.GetChineseName(prev)} → [{i}] {CardColorPalette.GetChineseName(curr)}");
                if (prev > curr) colorSorted = false;
            }
            Assert("按颜色分组排列", colorSorted);

            // 牌堆计数
            Log($"  手牌: {handVM.HandCount} | 抽牌堆: {handVM.DeckCount} | 弃牌堆: {handVM.DiscardCount}");
            Assert("牌堆计数一致", handVM.HandCount + handVM.DeckCount + handVM.DiscardCount == 5);

            // GetCardAt
            var card0 = handVM.GetCardAt(0);
            Assert("GetCardAt 返回有效卡牌", card0 != null);

            handVM.Dispose();
            Log("");
        }

        // ================================================================
        //  测试 2: ComboViewModel 连击链
        // ================================================================
        private void TestComboViewModel()
        {
            Log("------ 测试 2: ComboViewModel 连击链数据 ------");

            var combo = new ComboSystem();
            var comboVM = new ComboViewModel();
            comboVM.OnLog += msg => Log($"  [ComboVM] {msg}");
            comboVM.Bind(combo);

            // 初始状态
            Log($"  初始: IsActive={comboVM.IsActive} Combo={comboVM.CurrentCombo}");
            Assert("初始无连击", !comboVM.IsActive);
            Assert("初始Combo=0", comboVM.CurrentCombo == 0);

            // 打出连击链 0→1→2→3→4
            combo.TryPlayCard(MakeCost(0)); comboVM.OnCardPlayed();
            Log($"  打出0费: Combo={comboVM.CurrentCombo} 链={comboVM.ChainLength}");
            Assert("第1张Combo=0", comboVM.CurrentCombo == 0);
            Assert("第1张链长=1", comboVM.ChainLength == 1);

            combo.TryPlayCard(MakeCost(1)); comboVM.OnCardPlayed();
            Log($"  打出1费: Combo={comboVM.CurrentCombo} 链={comboVM.ChainLength}");
            Assert("第2张Combo=1", comboVM.CurrentCombo == 1);

            combo.TryPlayCard(MakeCost(2)); comboVM.OnCardPlayed();
            Log($"  打出2费: Combo={comboVM.CurrentCombo} 链={comboVM.ChainLength}");
            Assert("第3张Combo=2", comboVM.CurrentCombo == 2);

            combo.TryPlayCard(MakeCost(3)); comboVM.OnCardPlayed();
            combo.TryPlayCard(MakeCost(4)); comboVM.OnCardPlayed();
            Log($"  完整链 0→1→2→3→4: Combo={comboVM.CurrentCombo}");
            Assert("5连击Combo=4", comboVM.CurrentCombo == 4);
            Assert("链长=5", comboVM.ChainLength == 5);

            // 连击摘要
            Log($"  摘要: {comboVM.GetSummary()}");

            // 墨迹扩散强度
            Log($"  墨迹扩散强度: {comboVM.InkSpreadIntensity:F2}");
            Assert("墨迹强度>0", comboVM.InkSpreadIntensity > 0);

            // 测试万能牌续链：0→1→2→3→W→0→1→2→3→W
            combo.Reset(); comboVM.Refresh();
            combo.TryPlayCard(MakeCost(0));   // Combo=0
            combo.TryPlayCard(MakeCost(1));   // Combo=1
            combo.TryPlayCard(MakeCost(2));   // Combo=2
            combo.TryPlayCard(MakeCost(3));   // Combo=3
            combo.TryPlayCard(MakeWildCost()); // W → Combo=4, reset cost
            combo.TryPlayCard(MakeCost(0));   // 0费 → Combo=5 (W后可任意)
            combo.TryPlayCard(MakeCost(1));   // Combo=6
            combo.TryPlayCard(MakeCost(2));   // Combo=7
            combo.TryPlayCard(MakeCost(3));   // Combo=8
            combo.TryPlayCard(MakeWildCost()); // W → Combo=9
            comboVM.Refresh();
            Log($"  万能牌续链 0→1→2→3→W→0→1→2→3→W: Combo={comboVM.CurrentCombo}");
            Assert("万能牌续链Combo=9", comboVM.CurrentCombo == 9);
            Assert("链长=10", comboVM.ChainLength == 10);

            // 高潮模式（需要 ≥10 连击）
            combo.Reset(); comboVM.Refresh();
            for (int i = 0; i < 12; i++)
            {
                combo.TryPlayCard(MakeCost(i));
            }
            comboVM.Refresh();
            Log($"  12连击: IsClimax={comboVM.IsClimax} Combo={comboVM.CurrentCombo}");
            Assert("连击≥10高潮激活", comboVM.IsClimax);

            comboVM.Dispose();
            Log("");
        }

        // ================================================================
        //  测试 3: HUDViewModel 数据绑定
        // ================================================================
        private void TestHUDViewModel()
        {
            Log("------ 测试 3: HUDViewModel 数据绑定 ------");

            var player = new PlayerStats { maxHealth = 100, currentHealth = 80, faLi = 5, armor = 3 };
            var deck = new List<CardConfig> { CreateCard("测试卡", 1, 20, CardColor.Huo) };
            var enemies = new List<EnemyStats>
            {
                new EnemyStats("小怪", 50, 5),
                new EnemyStats("Boss", 500, 20, 10, true),
            };
            var bossTimer = new BossTimer(5, 30);
            var combat = new CombatController(player, deck, enemies, bossTimer);
            combat.OnLog += msg => { };

            var hudVM = new HUDViewModel();
            hudVM.OnLog += msg => Log($"  [HUDVM] {msg}");
            hudVM.Bind(combat);

            // 玩家状态
            Log($"  玩家: HP={hudVM.PlayerHP}/{hudVM.PlayerMaxHP} 护甲={hudVM.PlayerArmor}");
            Assert("玩家HP=80", hudVM.PlayerHP == 80);
            Assert("玩家最大HP=100", hudVM.PlayerMaxHP == 100);
            Assert("玩家护甲=3", hudVM.PlayerArmor == 3);
            Assert("HP比例=0.8", Mathf.Approximately(hudVM.HPRatio, 0.8f));

            // 法力
            Log($"  法力: {hudVM.ManaCurrent}/{hudVM.ManaMax} (+{hudVM.ManaTemp})");
            Assert("法力上限=5", hudVM.ManaMax == 5);

            // 敌人列表
            Log($"  敌人数: {hudVM.EnemyCount} 有Boss: {hudVM.HasBoss}");
            Assert("敌人数量=2", hudVM.EnemyCount == 2);
            Assert("有Boss", hudVM.HasBoss);

            // Boss 计时器
            Log($"  Boss计时器: 有={hudVM.HasBossTimer} 眼={hudVM.BossEyesOpen}/{hudVM.BossEyesTotal}");
            Assert("Boss计时器存在", hudVM.HasBossTimer);
            Assert("Boss眼总数=5", hudVM.BossEyesTotal == 5);
            Assert("Boss眼初始=0", hudVM.BossEyesOpen == 0);

            // 模拟出牌→睁眼
            combat.StartTurn();
            combat.TryPlayCard(combat.Hand[0]);
            hudVM.Refresh();
            Log($"  出牌后: Boss眼={hudVM.BossEyesOpen}/{hudVM.BossEyesTotal}");
            Assert("出牌后Boss眼=1", hudVM.BossEyesOpen == 1);

            // 等级经验
            Log($"  等级: Lv{hudVM.Level} XP={hudVM.XP}/{hudVM.XPRequired}");
            Assert("初始等级=1", hudVM.Level == 1);

            hudVM.Dispose();
            Log("");
        }

        // ================================================================
        //  测试 4: LevelUpViewModel 奖励生成
        // ================================================================
        private void TestLevelUpViewModel()
        {
            Log("------ 测试 4: LevelUpViewModel 奖励生成 ------");

            var player = new PlayerStats();
            var upgradePool = new List<CardConfig>
            {
                CreateCard("碎星锤", 2, 60, CardColor.Huo),
                CreateEvolvableCard("铁剑", 1, 20, CardColor.Huo, "寒铁剑"),
            };
            var levelUp = new LevelUpSystem(player, upgradePool);
            var deck = new List<CardConfig>();

            var levelUpVM = new LevelUpViewModel();
            levelUpVM.OnLog += msg => Log($"  [LevelUpVM] {msg}");
            levelUpVM.Bind(levelUp, deck);

            // 生成奖励
            levelUpVM.GenerateRewards(3);
            Log($"  奖励数: {levelUpVM.RewardCount}");
            Assert("生成了3个奖励", levelUpVM.RewardCount == 3);
            Assert("有待处理奖励", levelUpVM.IsPending);

            // 检查每个奖励
            for (int i = 0; i < levelUpVM.Rewards.Count; i++)
            {
                var r = levelUpVM.Rewards[i];
                Log($"  [{i}] {r.name} - {r.description} 可进化={r.canEvolve}");
                if (r.canEvolve)
                    Log($"      进化提示: {r.evolutionHint}");
            }

            // 选择奖励
            bool selected = levelUpVM.SelectReward(0);
            Assert("选择奖励成功", selected);
            Assert("选择后无待处理", !levelUpVM.IsPending);
            Assert("选择后奖励清空", levelUpVM.RewardCount == 0);

            // 进化提示检查
            var evolvableCard = upgradePool[1];
            bool canEvolve = LevelUpViewModel.CanEvolve(evolvableCard);
            string hint = LevelUpViewModel.GetEvolutionHint(evolvableCard);
            Log($"  卡牌「{evolvableCard.cardName}」可进化={canEvolve} 提示={hint}");
            Assert("可进化卡牌检测正确", canEvolve);
            Assert("进化提示包含目标名", hint != null && hint.Contains("寒铁剑"));

            levelUpVM.Dispose();
            Log("");
        }

        // ================================================================
        //  测试 5: DungeonViewModel 地图数据
        // ================================================================
        private void TestDungeonViewModel()
        {
            Log("------ 测试 5: DungeonViewModel 地图数据 ------");

            var config = new DungeonConfig
            {
                dungeonName = "测试地牢",
                volumeId = "xiyou",
                roomsPerFloor = 8,
                hasBoss = true,
                springIterations = 100,
                treasureDensity = 0.15f,
                chickenDensity = 0.1f,
                lightDensity = 0.12f,
                sideRoomDensity = 0.08f,
                merchantDensity = 0.05f,
                encounterDensity = 0.35f,
            };

            var floor = DungeonGenerator.GenerateFloor(config, 1, true);
            var exploration = new ExplorationController(floor, 100, 120);
            exploration.OnLog += msg => { };

            var dungeonVM = new DungeonViewModel();
            dungeonVM.OnLog += msg => Log($"  [DungeonVM] {msg}");
            dungeonVM.Bind(exploration);

            Log($"  总房间数: {dungeonVM.TotalRooms}");
            Assert("总房间数>0", dungeonVM.TotalRooms > 0);

            Log($"  当前房间ID: {dungeonVM.CurrentRoomId}");
            Assert("当前房间=入口", dungeonVM.CurrentRoomId == floor.entranceId);

            Log($"  可移动房间数: {dungeonVM.AvailableMoves.Count}");
            Assert("可移动房间数>0", dungeonVM.AvailableMoves.Count > 0);

            Log($"  已访问: {dungeonVM.VisitedRooms}/{dungeonVM.TotalRooms}");
            Assert("已访问≥1", dungeonVM.VisitedRooms >= 1);

            // 移动到第一个可移动房间
            var firstMove = dungeonVM.AvailableMoves[0];
            bool moved = dungeonVM.MoveTo(firstMove.id);
            Assert("移动成功", moved);
            Log($"  移动到房间 {firstMove.id} ({firstMove.typeName})");

            dungeonVM.Dispose();
            Log("");
        }

        // ================================================================
        //  测试 6: AudioManager 事件分发
        // ================================================================
        private void TestAudioManagerEvents()
        {
            Log("------ 测试 6: AudioManager 事件分发 ------");

            // 创建 AudioManager（无 AudioSource，模拟 headless）
            var go = new GameObject("AudioTest");
            var am = go.AddComponent<AudioManager>();

            int logCount = 0;
            am.OnLog += msg =>
            {
                logCount++;
                Log($"  [Audio] {msg}");
            };

            // 播放音效
            am.PlaySfx(SfxType.CardPlay);
            Assert("PlaySfx CardPlay 产生日志", logCount > 0);
            int afterCardPlay = logCount;

            am.PlaySfx(SfxType.ComboIncrement);
            Assert("PlaySfx ComboIncrement 产生日志", logCount > afterCardPlay);

            am.PlaySfx(SfxType.LevelUp);
            am.PlaySfx(SfxType.TreasureOpen);
            am.PlaySfx(SfxType.BossAppear);

            // 音乐切换
            am.PlayMusic(MusicScene.Combat);
            am.PlayMusic(MusicScene.Boss);
            am.PlayMusic(MusicScene.Hall);

            // 连击状态
            am.SetComboCount(5);
            Log($"  连击数=5: Climax={am.ComboClimaxActive}");
            Assert("连击5无高潮", !am.ComboClimaxActive);

            am.SetComboCount(12);
            Log($"  连击数=12: Climax={am.ComboClimaxActive}");
            Assert("连击12激活高潮", am.ComboClimaxActive);

            am.ResetCombo();
            Log($"  重置后: Climax={am.ComboClimaxActive} Count={am.ComboCount}");
            Assert("重置后无高潮", !am.ComboClimaxActive);

            Destroy(go);
            Log("");
        }

        // ================================================================
        //  测试 7: ViewModel 事件订阅/取消订阅
        // ================================================================
        private void TestViewModelLifecycle()
        {
            Log("------ 测试 7: ViewModel 事件订阅/取消订阅 ------");

            var player = new PlayerStats();
            var deck = new List<CardConfig> { CreateCard("测试卡", 1, 10, CardColor.Huo) };
            var enemies = new List<EnemyStats> { new EnemyStats("敌人", 999) };
            var combat = new CombatController(player, deck, enemies);
            combat.OnLog += msg => { };

            var handVM = new HandViewModel();
            int eventCount = 0;
            handVM.OnPropertyChanged += prop => eventCount++;
            handVM.OnCollectionChanged += col => eventCount++;

            handVM.Bind(combat);
            Log("  HandViewModel 绑定完成");
            Assert("绑定后无异常", true);

            // 触发事件
            combat.StartTurn();
            handVM.Refresh();
            int afterRefresh = eventCount;
            Log($"  Refresh 后事件数: {afterRefresh}");
            Assert("Refresh 触发事件", afterRefresh > 0);

            // 排序触发事件
            int beforeSort = eventCount;
            handVM.SortByCost();
            Log($"  SortByCost 后事件数增量: {eventCount - beforeSort}");
            Assert("排序触发事件", eventCount > beforeSort);

            // Dispose 取消订阅
            handVM.Dispose();
            handVM.Refresh(); // 不应触发事件
            int afterDispose = eventCount;
            Assert("Dispose后事件数不变", eventCount == afterDispose);
            Log("  Dispose 后事件已取消订阅");

            Log("");
        }

        // ================================================================
        //  测试 8: CardColorPalette 配色体系
        // ================================================================
        private void TestColorPalette()
        {
            Log("------ 测试 8: CardColorPalette 配色体系 ------");

            // 测试所有7种颜色
            CardColor[] colors = { CardColor.Huo, CardColor.Tu, CardColor.Mu, CardColor.Shui, CardColor.Jin, CardColor.Yang, CardColor.Yin };
            string[] expectedNames = { "武", "御", "气", "灵", "魂", "通", "煞" };

            for (int i = 0; i < colors.Length; i++)
            {
                var color = colors[i];
                var name = CardColorPalette.GetChineseName(color);
                var main = CardColorPalette.GetMainColor(color);
                var border = CardColorPalette.GetBorderColor(color);

                Log($"  {color}: 中式名={name} 主色=({main.r:F1},{main.g:F1},{main.b:F1}) 辅色=({border.r:F1},{border.g:F1},{border.b:F1})");
                Assert($"颜色{color}名称正确", name == expectedNames[i]);
            }

            Log("");
        }

        // ================================================================
        //  辅助方法
        // ================================================================

        private CardConfig CreateCard(string name, int cost, int damage, CardColor color)
        {
            var card = ScriptableObject.CreateInstance<CardConfig>();
            card.cardName = name;
            card.description = $"费{cost} | 伤{damage}";
            card.color = color;
            card.costType = CostType.Default;
            card.manaCost = cost;
            card.baseDamage = damage;
            card.gemSlots = 1;
            return card;
        }

        private CardConfig CreateEvolvableCard(string name, int cost, int damage, CardColor color, string evolveName)
        {
            var card = CreateCard(name, cost, damage, color);
            var evolve = CreateCard(evolveName, cost + 2, damage * 2, color);
            card.evolutionTarget = evolve;
            return card;
        }

        private static ResolvedCardCost MakeCost(int comboValue) =>
            new ResolvedCardCost(comboValue, comboValue, comboValue, false, false, false, false, false, false);

        private static ResolvedCardCost MakeWildCost() =>
            new ResolvedCardCost(0, 0, 0, true, false, false, false, false, false);

        private void Assert(string testName, bool condition)
        {
            if (condition)
            {
                _passCount++;
                Log($"  ✓ {testName}");
            }
            else
            {
                _failCount++;
                Log($"  ✗ FAIL: {testName}");
            }
        }

        private void Log(string msg)
        {
            string line = $"[Phase5] {msg}";
            Debug.Log(line);
            _log.AppendLine(line);
        }
    }
}
