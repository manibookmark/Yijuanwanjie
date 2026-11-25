using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 地牢探索控制器
    /// 管理玩家在楼层中的移动、交互、探索要素
    /// </summary>
    public class ExplorationController
    {
        private DungeonFloor _floor;
        private int _currentRoomId;
        private int _playerHP;
        private int _playerMaxHP;
        private int _playerArmor;
        private int _lingyu;        // 灵玉
        private int _lightsBroken;   // 已破坏光源数
        private int _chickensEaten; // 已吃灵丹数
        private readonly EconomySystem _economy;
        private readonly Func<int> _getGreed;

        public event Action<string> OnLog;
        public event Action<ExplorationEvent> OnEvent;

        public int CurrentRoomId => _currentRoomId;
        public Room CurrentRoom => _floor.GetRoom(_currentRoomId);
        public DungeonFloor Floor => _floor;
        public int PlayerHP => _playerHP;
        public int Lingyu => _lingyu;
        public int LightsBroken => _lightsBroken;
        public int ChickensEaten => _chickensEaten;

        public ExplorationController(DungeonFloor floor, int playerHP, int playerMaxHP,
            int playerArmor = 0, EconomySystem economy = null, Func<int> getGreed = null)
        {
            _floor = floor;
            _currentRoomId = floor.entranceId;
            _playerHP = playerHP;
            _playerMaxHP = playerMaxHP;
            _playerArmor = playerArmor;
            _lingyu = 0;
            _economy = economy;
            _getGreed = getGreed ?? (() => 0);

            // 标记入口已访问
            CurrentRoom.visited = true;
        }

        /// <summary>
        /// 获取可移动的房间
        /// </summary>
        public List<Room> GetAvailableMoves()
        {
            return _floor.GetUnvisitedConnected(_currentRoomId);
        }

        /// <summary>
        /// 移动到相邻房间
        /// </summary>
        public bool MoveTo(int roomId)
        {
            var connected = _floor.GetConnectedRooms(_currentRoomId);
            bool found = false;
            foreach (var r in connected)
            {
                if (r.id == roomId) { found = true; break; }
            }
            if (!found)
            {
                OnLog?.Invoke($"✗ 无法到达房间 {roomId}（不相邻）");
                return false;
            }

            _currentRoomId = roomId;
            CurrentRoom.visited = true;

            OnLog?.Invoke($"→ 移动到房间 [{CurrentRoom.x},{CurrentRoom.y}] ({GetRoomTypeName(CurrentRoom.type)})");
            Publish(ExplorationEventType.RoomEntered);

            // 触发房间内容
            TriggerRoomContent();

            return true;
        }

        /// <summary>
        /// 触发当前房间内容
        /// </summary>
        void TriggerRoomContent()
        {
            var room = CurrentRoom;
            if (room.cleared) return;

            switch (room.type)
            {
                case RoomType.Encounter:
                    OnLog?.Invoke($"  ⚔ 遭遇战！难度:{room.difficulty}");
                    Publish(ExplorationEventType.EncounterStarted);
                    // 战斗由 CombatController 处理，这里标记
                    break;

                case RoomType.Treasure:
                    int gold = UnityEngine.Random.Range(20, 80);
                    int granted = _economy != null
                        ? _economy.Grant(gold, Math.Max(0, _getGreed()), JadeSource.Special, true).FinalJade
                        : gold;
                    _lingyu += granted;
                    room.cleared = true;
                    OnLog?.Invoke($"  📦 宝箱！+{granted} 灵玉");
                    Publish(ExplorationEventType.TreasureOpened, granted);
                    break;

                case RoomType.Chicken:
                    int heal = 10;
                    _playerHP = Math.Min(_playerMaxHP, _playerHP + heal);
                    _chickensEaten++;
                    room.cleared = true;
                    OnLog?.Invoke($"  💊 灵丹！+{heal} HP → {_playerHP}/{_playerMaxHP}");
                    Publish(ExplorationEventType.PlayerHealed, heal);
                    break;

                case RoomType.Light:
                    OnLog?.Invoke($"  🔦 光源（可破坏，已破坏 {_lightsBroken} 个）");
                    break;

                case RoomType.SideRoom:
                    OnLog?.Invoke($"  ◆ 侧室事件！");
                    TriggerSideRoom();
                    room.cleared = true;
                    break;

                case RoomType.SealPlatform:
                    OnLog?.Invoke($"  🔒 封印台（可救英灵）");
                    break;

                case RoomType.GemShrine:
                    OnLog?.Invoke($"  💎 宝石神龛（可镶宝石/进化）");
                    break;

                case RoomType.Merchant:
                    OnLog?.Invoke($"  🏪 游方商（可删卡换属性）");
                    Publish(ExplorationEventType.MerchantEntered);
                    break;

                case RoomType.CashOut:
                    OnLog?.Invoke($"  💰 折现台（可折卡换灵玉）");
                    break;

                case RoomType.Exit:
                    OnLog?.Invoke($"  🛫 发现灵锹！可下楼");
                    break;

                case RoomType.Boss:
                    OnLog?.Invoke($"  👹 Boss 房！天劫守卫在此");
                    Publish(ExplorationEventType.BossEncountered);
                    break;
            }
        }

        void TriggerSideRoom()
        {
            int roll = UnityEngine.Random.Range(0, 4);
            switch (roll)
            {
                case 0:
                    int heal = 40;
                    _playerHP = Math.Min(_playerMaxHP, _playerHP + heal);
                    OnLog?.Invoke($"    灵泉！+{heal} HP → {_playerHP}/{_playerMaxHP}");
                    break;
                case 1:
                    int gems = 3;
                    OnLog?.Invoke($"    封印宝库！获得 {gems} 颗宝石（-20HP）");
                    _playerHP = Math.Max(1, _playerHP - 20);
                    break;
                case 2:
                    OnLog?.Invoke("    卷轴泉！+2 太极牌");
                    break;
                case 3:
                    OnLog?.Invoke("    游方铁匠！折扣镶嵌宝石");
                    break;
            }
        }

        /// <summary>
        /// 破坏光源
        /// </summary>
        public bool BreakLight()
        {
            if (CurrentRoom.type != RoomType.Light || CurrentRoom.cleared)
            {
                OnLog?.Invoke("✗ 此处无可破坏的光源");
                return false;
            }
            _lightsBroken++;
            CurrentRoom.cleared = true;
            OnLog?.Invoke($"  🔨 破坏光源！（累计 {_lightsBroken} 个）");
            Publish(ExplorationEventType.LightBroken);

            // 每 20 个解锁特定卡牌
            if (_lightsBroken == 20)
            {
                OnLog?.Invoke("  ★ 破坏 20 个光源！解锁新卡牌");
            }
            return true;
        }

        /// <summary>
        /// 吃灵丹
        /// </summary>
        public bool EatChicken()
        {
            if (CurrentRoom.type != RoomType.Chicken || CurrentRoom.cleared)
            {
                OnLog?.Invoke("✗ 此处无灵丹");
                return false;
            }
            _playerHP = Math.Min(_playerMaxHP, _playerHP + 10);
            _chickensEaten++;
            CurrentRoom.cleared = true;
            OnLog?.Invoke($"  💊 吃灵丹 +10HP → {_playerHP}/{_playerMaxHP}");
            Publish(ExplorationEventType.PlayerHealed, 10);

            if (_chickensEaten == 5)
            {
                OnLog?.Invoke("  ★ 吃 5 只灵丹！解锁新卡牌");
            }
            return true;
        }

        /// <summary>
        /// 使用灵锹下楼
        /// </summary>
        public bool UseShovel()
        {
            if (CurrentRoom.type != RoomType.Exit && CurrentRoom.type != RoomType.Boss)
            {
                OnLog?.Invoke("✗ 此处无法使用灵锹");
                return false;
            }
            OnLog?.Invoke($"  ⛏ 使用灵锹！下楼...");
            return true;
        }

        /// <summary>
        /// 检查楼层是否探索完毕（到达出口/Boss）
        /// </summary>
        public bool CanProceed()
        {
            return CurrentRoom.type == RoomType.Exit || CurrentRoom.type == RoomType.Boss;
        }

        /// <summary>
        /// 获取探索进度
        /// </summary>
        public string GetProgress()
        {
            int visited = 0;
            foreach (var r in _floor.rooms) if (r.visited) visited++;
            return $"探索: {visited}/{_floor.rooms.Count} | HP: {_playerHP}/{_playerMaxHP} | 灵玉: {_lingyu}";
        }

        public static string GetRoomTypeName(RoomType type)
        {
            return type switch
            {
                RoomType.Entrance => "入口",
                RoomType.Exit => "出口",
                RoomType.Boss => "Boss",
                RoomType.Encounter => "遭遇战",
                RoomType.Treasure => "宝箱",
                RoomType.Chicken => "灵丹",
                RoomType.Light => "光源",
                RoomType.SideRoom => "侧室",
                RoomType.SealPlatform => "封印台",
                RoomType.GemShrine => "宝石神龛",
                RoomType.Merchant => "游方商",
                RoomType.CashOut => "折现台",
                _ => "空"
            };
        }

        private void Publish(ExplorationEventType type, int amount = 0)
        {
            OnEvent?.Invoke(new ExplorationEvent(type, CurrentRoom, amount));
        }
    }
}
