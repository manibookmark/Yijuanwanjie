using System;
using System.Collections.Generic;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 地牢房间类型
    /// </summary>
    public enum RoomType
    {
        Empty,      // 空房间
        Entrance,   // 入口
        Exit,       // 出口（灵锹下楼）
        Encounter,  // 遭遇战
        Treasure,   // 宝箱
        Chicken,    // 灵丹（回血）
        Light,      // 光源（破坏解锁卡牌）
        SideRoom,   // 侧室事件
        SealPlatform,// 封印台（救英灵）
        GemShrine,  // 宝石神龛
        Merchant,   // 游方商
        CashOut,    // 折现台
        Boss        // Boss 房
    }

    /// <summary>
    /// 地牢房间
    /// </summary>
    [Serializable]
    public class Room
    {
        public int id;
        public int x, y;           // 网格坐标
        public RoomType type;
        public List<int> connections = new List<int>(); // 相连房间ID
        public bool visited = false;
        public bool cleared = false;  // 已清空/已拾取
        public int difficulty = 0;    // 遭遇战难度

        public Room(int id, int x, int y, RoomType type = RoomType.Empty)
        {
            this.id = id;
            this.x = x;
            this.y = y;
            this.type = type;
        }

        public bool IsExplored => visited && (cleared || type == RoomType.Empty || type == RoomType.Entrance);
    }

    /// <summary>
    /// 地牢楼层
    /// </summary>
    [Serializable]
    public class DungeonFloor
    {
        public int floorNumber;
        public List<Room> rooms = new List<Room>();
        public int entranceId;
        public int exitId;       // 灵锹位置
        public int? bossId;      // Boss 房（末层才有）

        // 探索要素统计
        public int treasureCount;
        public int chickenCount;
        public int lightCount;
        public int sideRoomCount;
        public int merchantCount;

        public Room GetRoom(int id) => rooms.Find(r => r.id == id);

        public Room Entrance => GetRoom(entranceId);

        public List<Room> GetConnectedRooms(int roomId)
        {
            var room = GetRoom(roomId);
            if (room == null) return new List<Room>();
            var result = new List<Room>();
            foreach (var cid in room.connections)
            {
                var r = GetRoom(cid);
                if (r != null) result.Add(r);
            }
            return result;
        }

        public List<Room> GetUnvisitedConnected(int roomId)
        {
            var result = new List<Room>();
            foreach (var r in GetConnectedRooms(roomId))
            {
                if (!r.visited) result.Add(r);
            }
            return result;
        }
    }

    /// <summary>
    /// 地牢配置
    /// </summary>
    [Serializable]
    public class DungeonConfig
    {
        public string dungeonName;
        public string volumeId;      // 卷属
        public int totalFloors;      // 楼层数
        public int difficulty;       // 难度 1-9
        public int roomsPerFloor;    // 每层房间数（基础值）
        public bool hasBoss;         // 末层是否有 Boss

        // 生成参数（参考 VC 反汇编）
        public int springIterations = 200;    // 弹簧生成迭代次数
        public int maxPathLength = 8;         // 主路径最大长度
        public float sidePathDifficulty = 0.8f; // 偏路难度系数

        // 探索要素密度
        public float treasureDensity = 0.15f;   // 15% 房间有宝箱
        public float chickenDensity = 0.10f;    // 10% 房间有灵丹
        public float lightDensity = 0.12f;      // 12% 房间有光源
        public float sideRoomDensity = 0.08f;   // 8% 房间有侧室
        public float merchantDensity = 0.05f;   // 5% 房间有商人
        public float encounterDensity = 0.35f;  // 35% 房间有遭遇战
    }

    /// <summary>
    /// 程序化地牢生成器
    /// 参考 VC 弹簧生成 + MST 路径
    /// </summary>
    public static class DungeonGenerator
    {
        static Random _rng = new Random();

        /// <summary>
        /// 生成一个完整地牢
        /// </summary>
        public static List<DungeonFloor> GenerateDungeon(DungeonConfig config, int? seed = null)
        {
            if (seed.HasValue) _rng = new Random(seed.Value);

            var floors = new List<DungeonFloor>();

            for (int f = 0; f < config.totalFloors; f++)
            {
                bool isLastFloor = (f == config.totalFloors - 1);
                var floor = GenerateFloor(config, f + 1, isLastFloor);
                floors.Add(floor);
            }

            return floors;
        }

        /// <summary>
        /// 生成一层
        /// </summary>
        public static DungeonFloor GenerateFloor(DungeonConfig config, int floorNum, bool isLast)
        {
            var floor = new DungeonFloor { floorNumber = floorNum };

            // 房间数量 = 基础值 + 楼层 * 难度系数
            int roomCount = config.roomsPerFloor + (int)(floorNum * config.difficulty * 0.5f);
            roomCount = Math.Max(roomCount, 6); // 最少 6 间

            // 1. 弹簧生成：随机放置房间
            var positions = new HashSet<(int, int)>();
            var rooms = new List<Room>();
            int roomId = 0;

            // 入口
            var entrance = new Room(roomId++, 0, 0, RoomType.Entrance);
            rooms.Add(entrance);
            positions.Add((0, 0));
            floor.entranceId = entrance.id;

            // 弹簧迭代生成房间
            for (int i = 0; i < config.springIterations && rooms.Count < roomCount; i++)
            {
                // 从已有房间随机选一个，向随机方向扩展
                var baseRoom = rooms[_rng.Next(rooms.Count)];
                int dx = _rng.Next(-1, 2);
                int dy = _rng.Next(-1, 2);
                if (dx == 0 && dy == 0) continue;

                int nx = baseRoom.x + dx;
                int ny = baseRoom.y + dy;
                var pos = (nx, ny);

                if (positions.Contains(pos)) continue;

                positions.Add(pos);
                var room = new Room(roomId++, nx, ny);
                rooms.Add(room);
            }

            // 2. 构建连接（MST 简化版：每个房间连接最近的 1-2 个房间）
            for (int i = 1; i < rooms.Count; i++)
            {
                var room = rooms[i];
                // 找最近的房间
                int nearest = -1;
                double nearestDist = double.MaxValue;
                for (int j = 0; j < rooms.Count; j++)
                {
                    if (i == j) continue;
                    double dist = Math.Sqrt(
                        Math.Pow(room.x - rooms[j].x, 2) +
                        Math.Pow(room.y - rooms[j].y, 2));
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearest = j;
                    }
                }
                if (nearest >= 0 && !room.connections.Contains(rooms[nearest].id))
                {
                    room.connections.Add(rooms[nearest].id);
                    rooms[nearest].connections.Add(room.id);
                }

                // 有概率连第二个房间（增加路径多样性）
                if (_rng.NextDouble() < 0.3)
                {
                    int second = -1;
                    double secondDist = double.MaxValue;
                    for (int j = 0; j < rooms.Count; j++)
                    {
                        if (i == j || j == nearest || room.connections.Contains(rooms[j].id)) continue;
                        double dist = Math.Sqrt(
                            Math.Pow(room.x - rooms[j].x, 2) +
                            Math.Pow(room.y - rooms[j].y, 2));
                        if (dist < secondDist && dist > nearestDist)
                        {
                            secondDist = dist;
                            second = j;
                        }
                    }
                    if (second >= 0)
                    {
                        room.connections.Add(rooms[second].id);
                        rooms[second].connections.Add(room.id);
                    }
                }
            }

            // 3. 确保入口连通性：BFS 检查，不连通的房间加连接
            EnsureConnectivity(rooms, floor.entranceId);

            // 4. 分配房间类型
            // 找最远的房间作为出口/Boss
            int exitRoomId = FindFarthestRoom(rooms, floor.entranceId);
            var exitRoom = rooms[exitRoomId];

            if (isLast && config.hasBoss)
            {
                exitRoom.type = RoomType.Boss;
                floor.bossId = exitRoomId;
                floor.exitId = exitRoomId;
            }
            else
            {
                exitRoom.type = RoomType.Exit;
                floor.exitId = exitRoomId;
            }

            // 其他房间按密度分配
            var availableRooms = new List<Room>();
            foreach (var r in rooms)
            {
                if (r.type == RoomType.Empty) availableRooms.Add(r);
            }

            AssignRoomTypes(availableRooms, config, floor, _rng);

            floor.rooms = rooms;
            return floor;
        }

        /// <summary>
        /// 按密度分配房间类型
        /// </summary>
        static void AssignRoomTypes(List<Room> available, DungeonConfig config, DungeonFloor floor, Random rng)
        {
            // 打乱
            for (int i = available.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (available[i], available[j]) = (available[j], available[i]);
            }

            int idx = 0;
            int total = available.Count;

            // 遭遇战
            int encounterCount = Math.Max(1, (int)(total * config.encounterDensity));
            for (int i = 0; i < encounterCount && idx < total; i++, idx++)
            {
                available[idx].type = RoomType.Encounter;
                available[idx].difficulty = rng.Next(1, config.difficulty + 2);
            }

            // 宝箱
            int treasureCount = (int)(total * config.treasureDensity);
            for (int i = 0; i < treasureCount && idx < total; i++, idx++)
            {
                available[idx].type = RoomType.Treasure;
                floor.treasureCount++;
            }

            // 灵丹
            int chickenCount = (int)(total * config.chickenDensity);
            for (int i = 0; i < chickenCount && idx < total; i++, idx++)
            {
                available[idx].type = RoomType.Chicken;
                floor.chickenCount++;
            }

            // 光源
            int lightCount = (int)(total * config.lightDensity);
            for (int i = 0; i < lightCount && idx < total; i++, idx++)
            {
                available[idx].type = RoomType.Light;
                floor.lightCount++;
            }

            // 侧室
            int sideRoomCount = (int)(total * config.sideRoomDensity);
            for (int i = 0; i < sideRoomCount && idx < total; i++, idx++)
            {
                available[idx].type = RoomType.SideRoom;
                floor.sideRoomCount++;
            }

            // 商人
            int merchantCount = Math.Max(1, (int)(total * config.merchantDensity));
            for (int i = 0; i < merchantCount && idx < total; i++, idx++)
            {
                available[idx].type = RoomType.Merchant;
                floor.merchantCount++;
            }

            // 偶尔放封印台/宝石神龛/折现台
            if (rng.NextDouble() < 0.3 && idx < total)
            {
                available[idx++].type = RoomType.SealPlatform;
            }
            if (rng.NextDouble() < 0.4 && idx < total)
            {
                available[idx++].type = RoomType.GemShrine;
            }
            if (rng.NextDouble() < 0.5 && idx < total)
            {
                available[idx++].type = RoomType.CashOut;
            }
        }

        /// <summary>
        /// BFS 确保所有房间从入口可达
        /// </summary>
        static void EnsureConnectivity(List<Room> rooms, int entranceId)
        {
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(entranceId);
            visited.Add(entranceId);

            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                var room = rooms.Find(r => r.id == id);
                if (room == null) continue;
                foreach (var cid in room.connections)
                {
                    if (!visited.Contains(cid))
                    {
                        visited.Add(cid);
                        queue.Enqueue(cid);
                    }
                }
            }

            // 不连通的房间连到最近的已连通房间
            foreach (var room in rooms)
            {
                if (!visited.Contains(room.id))
                {
                    // 找最近的已连通房间
                    int nearest = -1;
                    double nearestDist = double.MaxValue;
                    foreach (var v in visited)
                    {
                        var vr = rooms.Find(r => r.id == v);
                        if (vr == null) continue;
                        double dist = Math.Sqrt(
                            Math.Pow(room.x - vr.x, 2) +
                            Math.Pow(room.y - vr.y, 2));
                        if (dist < nearestDist)
                        {
                            nearestDist = dist;
                            nearest = vr.id;
                        }
                    }
                    if (nearest >= 0)
                    {
                        room.connections.Add(nearest);
                        rooms.Find(r => r.id == nearest)?.connections.Add(room.id);
                        visited.Add(room.id);
                    }
                }
            }
        }

        /// <summary>
        /// 找距离入口最远的房间（BFS）
        /// </summary>
        static int FindFarthestRoom(List<Room> rooms, int entranceId)
        {
            var dist = new Dictionary<int, int>();
            var queue = new Queue<int>();
            queue.Enqueue(entranceId);
            dist[entranceId] = 0;

            int farthest = entranceId;
            int maxDist = 0;

            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                var room = rooms.Find(r => r.id == id);
                if (room == null) continue;
                foreach (var cid in room.connections)
                {
                    if (!dist.ContainsKey(cid))
                    {
                        dist[cid] = dist[id] + 1;
                        if (dist[cid] > maxDist)
                        {
                            maxDist = dist[cid];
                            farthest = cid;
                        }
                        queue.Enqueue(cid);
                    }
                }
            }

            return farthest;
        }

        /// <summary>
        /// 打印地牢地图（ASCII）
        /// </summary>
        public static string PrintFloorMap(DungeonFloor floor)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== 楼层 {floor.floorNumber} ===");

            // 找坐标范围
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (var r in floor.rooms)
            {
                if (r.x < minX) minX = r.x;
                if (r.x > maxX) maxX = r.x;
                if (r.y < minY) minY = r.y;
                if (r.y > maxY) maxY = r.y;
            }

            int w = maxX - minX + 1;
            int h = maxY - minY + 1;
            var grid = new string[w, h];

            foreach (var r in floor.rooms)
            {
                int gx = r.x - minX;
                int gy = r.y - minY;
                grid[gx, gy] = r.type switch
                {
                    RoomType.Entrance => "[入]",
                    RoomType.Exit => "[出]",
                    RoomType.Boss => "[王]",
                    RoomType.Encounter => "[战]",
                    RoomType.Treasure => "[箱]",
                    RoomType.Chicken => "[丹]",
                    RoomType.Light => "[光]",
                    RoomType.SideRoom => "[室]",
                    RoomType.SealPlatform => "[封]",
                    RoomType.GemShrine => "[龛]",
                    RoomType.Merchant => "[商]",
                    RoomType.CashOut => "[折]",
                    _ => "[·]"
                };
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    sb.Append(grid[x, y] ?? "   ");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"房间数:{floor.rooms.Count} | 宝箱:{floor.treasureCount} 灵丹:{floor.chickenCount} " +
                          $"光源:{floor.lightCount} 侧室:{floor.sideRoomCount} 商人:{floor.merchantCount}");
            return sb.ToString();
        }
    }
}
