using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Dungeon;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 地牢地图视图
    /// 可视化网格地图，替代 ASCII PrintFloorMap
    /// GDD 11.4: 手绘2D贴图，URP 2D Light 渲染光影
    /// </summary>
    public class DungeonMapView : UIView
    {
        [Header("地图布局")]
        public RectTransform mapContainer;
        public float cellSize = 60f;
        public float cellSpacing = 5f;

        [Header("房间图标")]
        public Sprite entranceIcon;
        public Sprite exitIcon;
        public Sprite bossIcon;
        public Sprite encounterIcon;
        public Sprite treasureIcon;
        public Sprite chickenIcon;
        public Sprite lightIcon;
        public Sprite sideRoomIcon;
        public Sprite sealPlatformIcon;
        public Sprite gemShrineIcon;
        public Sprite merchantIcon;
        public Sprite cashOutIcon;
        public Sprite emptyIcon;

        [Header("连线")]
        public Color connectionColor = new Color(0.6f, 0.5f, 0.3f, 0.5f);
        public Color accessibleColor = new Color(0.9f, 0.8f, 0.5f, 0.8f);
        public Color currentRoomColor = new Color(0.9f, 0.3f, 0.2f, 1f);
        public Color visitedColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        public Color unvisitedColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

        private DungeonViewModel _dungeonVM;
        private GameObject[] _roomCells;

        protected override void OnViewModelChanged(string propertyName)
        {
            if (propertyName == "*" || propertyName == DungeonViewModel.PROP_MAP ||
                propertyName == DungeonViewModel.PROP_CURRENT_ROOM ||
                propertyName == DungeonViewModel.PROP_AVAILABLE_MOVES)
            {
                RefreshMap();
            }
        }

        public override void Bind(ViewModelBase viewModel)
        {
            base.Bind(viewModel);
            _dungeonVM = viewModel as DungeonViewModel;
        }

        /// <summary>
        /// 刷新地图渲染
        /// </summary>
        private void RefreshMap()
        {
            if (_dungeonVM == null || mapContainer == null) return;

            var rooms = _dungeonVM.AllRooms;
            int count = rooms.Count;

            // 清除旧的
            for (int i = mapContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(mapContainer.GetChild(i).gameObject);
            }

            // 计算偏移居中
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                if (r.x < minX) minX = r.x;
                if (r.x > maxX) maxX = r.x;
                if (r.y < minY) minY = r.y;
                if (r.y > maxY) maxY = r.y;
            }

            float offsetX = -(minX + maxX) / 2f * (cellSize + cellSpacing);
            float offsetY = -(minY + maxY) / 2f * (cellSize + cellSpacing);

            // 绘制连线
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                Vector2 pos = new Vector2(
                    room.x * (cellSize + cellSpacing) + offsetX,
                    -room.y * (cellSize + cellSpacing) + offsetY
                );

                foreach (var connId in room.connections)
                {
                    if (connId <= room.id) continue; // 避免重复

                    var other = rooms[connId];
                    Vector2 otherPos = new Vector2(
                        other.x * (cellSize + cellSpacing) + offsetX,
                        -other.y * (cellSize + cellSpacing) + offsetY
                    );

                    DrawConnection(pos, otherPos);
                }
            }

            // 绘制房间
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                Vector2 pos = new Vector2(
                    room.x * (cellSize + cellSpacing) + offsetX,
                    -room.y * (cellSize + cellSpacing) + offsetY
                );

                var cell = CreateRoomCell(room, pos);
            }
        }

        /// <summary>
        /// 绘制连线
        /// </summary>
        private void DrawConnection(Vector2 from, Vector2 to)
        {
            var go = new GameObject("Connection", typeof(Image)).GetComponent<RectTransform>();
            go.SetParent(mapContainer, false);

            Vector2 mid = (from + to) / 2f;
            go.localPosition = mid;

            Vector2 dir = to - from;
            float length = dir.magnitude;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            go.sizeDelta = new Vector2(length, 3f);
            go.localRotation = Quaternion.Euler(0, 0, angle);
            go.GetComponent<Image>().color = connectionColor;
        }

        /// <summary>
        /// 创建房间格子
        /// </summary>
        private GameObject CreateRoomCell(RoomDisplayData room, Vector2 pos)
        {
            var go = new GameObject($"Room_{room.id}", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(mapContainer, false);
            rt.sizeDelta = new Vector2(cellSize, cellSize);
            rt.localPosition = pos;

            var img = go.GetComponent<Image>();
            img.sprite = GetRoomSprite(room.type);

            // 颜色
            if (room.isCurrent)
                img.color = currentRoomColor;
            else if (room.isAccessible)
                img.color = accessibleColor;
            else if (room.visited)
                img.color = visitedColor;
            else
                img.color = unvisitedColor;

            // 房间名文字
            var labelObj = new GameObject("Label", typeof(Text)).GetComponent<RectTransform>();
            labelObj.SetParent(go.transform, false);
            labelObj.anchorMin = Vector2.zero;
            labelObj.anchorMax = Vector2.one;
            labelObj.offsetMin = Vector2.zero;
            labelObj.offsetMax = Vector2.zero;
            var label = labelObj.GetComponent<Text>();
            label.text = room.typeName;
            label.fontSize = 10;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;

            // 可移动房间可点击
            if (room.isAccessible)
            {
                var button = go.AddComponent<Button>();
                int capturedId = room.id;
                button.onClick.AddListener(() => _dungeonVM.MoveTo(capturedId));
            }

            return go;
        }

        /// <summary>
        /// 获取房间类型图标
        /// </summary>
        private Sprite GetRoomSprite(RoomType type)
        {
            return type switch
            {
                RoomType.Entrance => entranceIcon,
                RoomType.Exit => exitIcon,
                RoomType.Boss => bossIcon,
                RoomType.Encounter => encounterIcon,
                RoomType.Treasure => treasureIcon,
                RoomType.Chicken => chickenIcon,
                RoomType.Light => lightIcon,
                RoomType.SideRoom => sideRoomIcon,
                RoomType.SealPlatform => sealPlatformIcon,
                RoomType.GemShrine => gemShrineIcon,
                RoomType.Merchant => merchantIcon,
                RoomType.CashOut => cashOutIcon,
                _ => emptyIcon
            };
        }
    }
}
