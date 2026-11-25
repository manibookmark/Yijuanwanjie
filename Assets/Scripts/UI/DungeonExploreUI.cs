using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using WanjieScroll.Dungeon;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 地牢探索界面
    /// 显示：地牢小地图、当前房间信息、可移动方向、探索要素交互
    /// </summary>
    public class DungeonExploreUI : MonoBehaviour
    {
        [Header("Map")]
        public RectTransform mapContainer;
        public GameObject roomIconPrefab;
        public float roomIconSize = 40f;
        public float roomSpacing = 50f;

        [Header("Current Room")]
        public TMP_Text roomNameText;
        public TMP_Text roomTypeText;
        public Image roomTypeIcon;

        [Header("Movement")]
        public Transform directionContainer;
        public Button[] directionButtons; // 上/下/左/右/确认

        [Header("Interaction")]
        public GameObject interactionPanel;
        public Button breakLightButton;
        public Button eatChickenButton;
        public Button useShovelButton;
        public Button openChestButton;
        public TMP_Text interactionText;

        [Header("HUD")]
        public TMP_Text hpText;
        public TMP_Text jadeText;
        public TMP_Text floorText;
        public TMP_Text exploreProgressText;

        [Header("Room Icons")]
        public Sprite entranceIcon;
        public Sprite exitIcon;
        public Sprite bossIcon;
        public Sprite encounterIcon;
        public Sprite treasureIcon;
        public Sprite chickenIcon;
        public Sprite lightIcon;
        public Sprite sideRoomIcon;
        public Sprite emptyIcon;

        private ExplorationController _exploration;
        private Dictionary<int, GameObject> _roomIcons = new Dictionary<int, GameObject>();

        public void Setup(ExplorationController exploration)
        {
            _exploration = exploration;
            RenderMap();
            UpdateCurrentRoom();
            UpdateHUD();
        }

        /// <summary>
        /// 渲染小地图
        /// </summary>
        public void RenderMap()
        {
            if (mapContainer == null) return;

            // 清除旧图标
            foreach (var kvp in _roomIcons)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _roomIcons.Clear();

            var floor = _exploration.Floor;
            int minX = 999, maxX = -999, minY = 999, maxY = -999;
            foreach (var r in floor.rooms)
            {
                if (r.x < minX) minX = r.x;
                if (r.x > maxX) maxX = r.x;
                if (r.y < minY) minY = r.y;
                if (r.y > maxY) maxY = r.y;
            }

            foreach (var room in floor.rooms)
            {
                if (roomIconPrefab == null) continue;

                var icon = Instantiate(roomIconPrefab, mapContainer);
                var rect = icon.GetComponent<RectTransform>();
                if (rect != null)
                {
                    float px = (room.x - minX) * roomSpacing - ((maxX - minX) * roomSpacing / 2f);
                    float py = (room.y - minY) * roomSpacing - ((maxY - minY) * roomSpacing / 2f);
                    rect.anchoredPosition = new Vector2(px, py);
                }

                // 设置图标
                var img = icon.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = GetRoomSprite(room.type);
                    // 已访问/未访问
                    img.color = room.visited ? Color.white : new Color(0.3f, 0.3f, 0.3f, 0.5f);
                }

                // 当前房间高亮
                if (room.id == _exploration.CurrentRoomId)
                {
                    var outline = icon.GetComponent<Outline>();
                    if (outline == null) outline = icon.AddComponent<Outline>();
                    outline.effectColor = Color.yellow;
                    outline.enabled = true;
                }

                _roomIcons[room.id] = icon;
            }
        }

        /// <summary>
        /// 更新当前房间信息
        /// </summary>
        public void UpdateCurrentRoom()
        {
            var room = _exploration.CurrentRoom;
            if (room == null) return;

            if (roomNameText != null)
                roomNameText.text = $"[{room.x},{room.y}]";
            if (roomTypeText != null)
                roomTypeText.text = ExplorationController.GetRoomTypeName(room.type);

            // 交互按钮
            bool canBreakLight = room.type == RoomType.Light && !room.cleared;
            bool canEatChicken = room.type == RoomType.Chicken && !room.cleared;
            bool canShovel = room.type == RoomType.Exit || room.type == RoomType.Boss;

            if (breakLightButton != null) breakLightButton.gameObject.SetActive(canBreakLight);
            if (eatChickenButton != null) eatChickenButton.gameObject.SetActive(canEatChicken);
            if (useShovelButton != null) useShovelButton.gameObject.SetActive(canShovel);

            // 方向按钮
            UpdateDirectionButtons();
        }

        /// <summary>
        /// 更新可移动方向
        /// </summary>
        void UpdateDirectionButtons()
        {
            if (directionContainer == null) return;

            var moves = _exploration.GetAvailableMoves();

            // 清除旧按钮
            for (int i = directionContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(directionContainer.GetChild(i).gameObject);
            }

            // 为每个可移动房间创建按钮
            foreach (var room in moves)
            {
                if (roomIconPrefab == null) continue;

                var btnObj = new GameObject($"MoveTo_{room.id}");
                btnObj.transform.SetParent(directionContainer, false);

                var btn = btnObj.AddComponent<Button>();
                var txt = btnObj.AddComponent<TMP_Text>(); // 需要 TMP

                var textComp = btnObj.AddComponent<TextMeshProUGUI>();
                textComp.text = $"{ExplorationController.GetRoomTypeName(room.type)}\n[{room.x},{room.y}]";
                textComp.fontSize = 14;
                textComp.alignment = TextAlignmentOptions.Center;

                int targetId = room.id;
                btn.onClick.AddListener(() =>
                {
                    _exploration.MoveTo(targetId);
                    RenderMap();
                    UpdateCurrentRoom();
                    UpdateHUD();
                });
            }
        }

        /// <summary>
        /// 更新 HUD
        /// </summary>
        public void UpdateHUD()
        {
            if (hpText != null)
                hpText.text = $"HP: {_exploration.PlayerHP}";
            if (jadeText != null)
                jadeText.text = $"Jade: {_exploration.Lingyu}";
            if (floorText != null)
                floorText.text = $"Floor: {_exploration.Floor.floorNumber}";
            if (exploreProgressText != null)
                exploreProgressText.text = _exploration.GetProgress();
        }

        Sprite GetRoomSprite(RoomType type)
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
                _ => emptyIcon
            };
        }
    }
}
