using System;
using System.Collections.Generic;
using WanjieScroll.Dungeon;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 地牢探索 ViewModel
    /// 管理地图数据、当前房间、可移动房间列表、探索进度
    /// 替代 ASCII PrintFloorMap 为结构化数据
    /// </summary>
    public class DungeonViewModel : ViewModelBase
    {
        private ExplorationController _exploration;
        private List<RoomDisplayData> _allRooms = new List<RoomDisplayData>();
        private List<RoomDisplayData> _availableMoves = new List<RoomDisplayData>();

        public const string PROP_MAP = "Map";
        public const string PROP_CURRENT_ROOM = "CurrentRoom";
        public const string PROP_AVAILABLE_MOVES = "AvailableMoves";
        public const string PROP_PROGRESS = "Progress";

        // ====== 进度 ======
        public int PlayerHP => _exploration?.PlayerHP ?? 0;
        public int Lingyu => _exploration?.Lingyu ?? 0;
        public int CurrentRoomId => _exploration?.CurrentRoomId ?? 0;
        public int TotalRooms => _allRooms.Count;
        public int VisitedRooms
        {
            get
            {
                int count = 0;
                foreach (var r in _allRooms) if (r.visited) count++;
                return count;
            }
        }

        // ====== 房间数据 ======
        public IReadOnlyList<RoomDisplayData> AllRooms => _allRooms;
        public IReadOnlyList<RoomDisplayData> AvailableMoves => _availableMoves;
        public RoomDisplayData? CurrentRoom
        {
            get
            {
                if (_exploration == null) return null;
                var room = _exploration.CurrentRoom;
                return RoomDisplayData.FromRoom(room, true, false);
            }
        }

        public bool CanProceed => _exploration?.CanProceed() ?? false;
        public int LightsBroken => _exploration?.LightsBroken ?? 0;
        public int ChickensEaten => _exploration?.ChickensEaten ?? 0;

        /// <summary>
        /// 绑定探索控制器
        /// </summary>
        public void Bind(ExplorationController exploration)
        {
            _exploration = exploration;
            _exploration.OnEvent += OnExplorationEvent;
            Refresh();
            Log("✓ DungeonViewModel 已绑定");
        }

        public override void Dispose()
        {
            if (_exploration != null)
            {
                _exploration.OnEvent -= OnExplorationEvent;
                _exploration = null;
            }
            base.Dispose();
        }

        private void OnExplorationEvent(ExplorationEvent explorationEvent)
        {
            Refresh();
        }

        /// <summary>
        /// 刷新地图数据
        /// </summary>
        public void Refresh()
        {
            if (_exploration == null || _exploration.Floor == null) return;

            _allRooms.Clear();
            _availableMoves.Clear();

            int currentId = _exploration.CurrentRoomId;
            var connected = new HashSet<int>();
            foreach (var move in _exploration.GetAvailableMoves())
            {
                connected.Add(move.id);
            }

            foreach (var room in _exploration.Floor.rooms)
            {
                bool isCurrent = room.id == currentId;
                bool isAccessible = connected.Contains(room.id) && !room.visited;
                _allRooms.Add(RoomDisplayData.FromRoom(room, isCurrent, isAccessible));

                if (isAccessible)
                {
                    _availableMoves.Add(RoomDisplayData.FromRoom(room, false, true));
                }
            }

            NotifyCollectionChanged(PROP_MAP);
            NotifyCollectionChanged(PROP_AVAILABLE_MOVES);
            RaisePropertyChanged(PROP_CURRENT_ROOM);
            RaisePropertyChanged(PROP_PROGRESS);
        }

        /// <summary>
        /// 移动到指定房间
        /// </summary>
        public bool MoveTo(int roomId)
        {
            if (_exploration == null) return false;
            bool success = _exploration.MoveTo(roomId);
            Refresh();
            return success;
        }

        /// <summary>
        /// 破坏光源
        /// </summary>
        public bool BreakLight()
        {
            if (_exploration == null) return false;
            bool success = _exploration.BreakLight();
            Refresh();
            return success;
        }

        /// <summary>
        /// 吃灵丹
        /// </summary>
        public bool EatChicken()
        {
            if (_exploration == null) return false;
            bool success = _exploration.EatChicken();
            Refresh();
            return success;
        }

        /// <summary>
        /// 使用灵锹下楼
        /// </summary>
        public bool UseShovel()
        {
            if (_exploration == null) return false;
            return _exploration.UseShovel();
        }

        public override void RefreshAll()
        {
            Refresh();
            base.RefreshAll();
        }
    }
}
