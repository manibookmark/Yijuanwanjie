using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Dungeon;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 探索 HUD
    /// 房间交互按钮（宝箱/灵丹/光源/商/封印台等）、进度显示、移动按钮
    /// GDD 11.5: 印章风格按钮，卷轴/竹简背景
    /// </summary>
    public class ExplorationHUD : UIView
    {
        [Header("进度")]
        public Text progressText;
        public Text hpText;
        public Text lingyuText;
        public Image lingyuIcon;

        [Header("移动按钮")]
        public RectTransform moveButtonContainer;
        public Button moveButtonPrefab;

        [Header("交互按钮")]
        public Button breakLightButton;
        public Button eatChickenButton;
        public Button useShovelButton;

        [Header("房间信息")]
        public Text currentRoomText;
        public Text roomDescriptionText;

        private DungeonViewModel _dungeonVM;
        private Coroutine _jadeGainRoutine;
        private GameObject _jadeGainVisual;
        private int _jadeGainVersion;

        protected override void OnViewModelChanged(string propertyName)
        {
            if (propertyName == "*" || propertyName == DungeonViewModel.PROP_CURRENT_ROOM ||
                propertyName == DungeonViewModel.PROP_PROGRESS)
            {
                RefreshProgress();
                RefreshRoomInfo();
                RefreshInteractionButtons();
            }

            if (propertyName == "*" || propertyName == DungeonViewModel.PROP_AVAILABLE_MOVES)
            {
                RefreshMoveButtons();
            }
        }

        public override void Bind(ViewModelBase viewModel)
        {
            base.Bind(viewModel);
            _dungeonVM = viewModel as DungeonViewModel;

            if (breakLightButton != null)
                breakLightButton.onClick.AddListener(() => _dungeonVM.BreakLight());
            if (eatChickenButton != null)
                eatChickenButton.onClick.AddListener(() => _dungeonVM.EatChicken());
            if (useShovelButton != null)
                useShovelButton.onClick.AddListener(() => _dungeonVM.UseShovel());
        }

        public override void Unbind()
        {
            CancelJadeGain();
            if (breakLightButton != null) breakLightButton.onClick.RemoveAllListeners();
            if (eatChickenButton != null) eatChickenButton.onClick.RemoveAllListeners();
            if (useShovelButton != null) useShovelButton.onClick.RemoveAllListeners();
            _dungeonVM = null;
            base.Unbind();
        }

        public void PlayJadeGain(int amount)
        {
            if (amount <= 0) return;
            CancelJadeGain();
            if (!isActiveAndEnabled || lingyuIcon == null) return;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            int version = _jadeGainVersion;
            Coroutine started = StartCoroutine(AnimateJadeGain(canvas, version));
            if (version == _jadeGainVersion && _jadeGainVisual != null)
                _jadeGainRoutine = started;
        }

        private IEnumerator AnimateJadeGain(Canvas canvas, int version)
        {
            var go = new GameObject("MapJadeGain", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            _jadeGainVisual = go;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.sizeDelta = new Vector2(42f, 42f);
            rt.position = lingyuIcon.rectTransform.position + Vector3.left * 180f + Vector3.down * 90f;
            Image image = go.GetComponent<Image>();
            image.sprite = lingyuIcon.sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            Vector3 start = rt.position;
            Vector3 end = lingyuIcon.rectTransform.position;
            for (int i = 0; i < 30; i++)
            {
                if (version != _jadeGainVersion || go == null || lingyuIcon == null)
                    break;
                float p = (i + 1) / 30f;
                float eased = 1f - Mathf.Pow(1f - p, 3f);
                rt.position = Vector3.Lerp(start, end, eased);
                rt.localScale = Vector3.one * (1f + 0.24f * Mathf.Sin(p * Mathf.PI));
                group.alpha = p < 0.8f ? 1f : 1f - (p - 0.8f) / 0.2f;
                yield return null;
            }
            if (version == _jadeGainVersion)
            {
                _jadeGainRoutine = null;
                ReleaseJadeGainVisual();
            }
        }

        private void CancelJadeGain()
        {
            unchecked { _jadeGainVersion++; }
            Coroutine previous = _jadeGainRoutine;
            _jadeGainRoutine = null;
            if (previous != null) StopCoroutine(previous);
            ReleaseJadeGainVisual();
        }

        private void ReleaseJadeGainVisual()
        {
            GameObject visual = _jadeGainVisual;
            _jadeGainVisual = null;
            if (visual == null) return;
            visual.SetActive(false);
            Destroy(visual);
        }

        private void OnDisable() => CancelJadeGain();

        /// <summary>
        /// 刷新进度
        /// </summary>
        private void RefreshProgress()
        {
            if (_dungeonVM == null) return;

            if (progressText != null)
                progressText.text = $"探索: {_dungeonVM.VisitedRooms}/{_dungeonVM.TotalRooms}";
            if (hpText != null)
                hpText.text = $"HP: {_dungeonVM.PlayerHP}";
            if (lingyuText != null)
                lingyuText.text = $"灵玉: {_dungeonVM.Lingyu}";
        }

        /// <summary>
        /// 刷新房间信息
        /// </summary>
        private void RefreshRoomInfo()
        {
            if (_dungeonVM == null) return;

            var currentRoom = _dungeonVM.CurrentRoom;
            if (!currentRoom.HasValue) return;
            var room = currentRoom.Value;

            if (currentRoomText != null)
            {
                currentRoomText.text = room.cleared ? $"{room.typeName} (已清理)" : room.typeName;
            }

            if (roomDescriptionText != null)
            {
                roomDescriptionText.text = room.type switch
                {
                    RoomType.Encounter => "遭遇战！准备战斗",
                    RoomType.Treasure => "宝箱！打开获取灵玉",
                    RoomType.Chicken => "灵丹！食用恢复HP",
                    RoomType.Light => "光源！破坏可解锁卡牌",
                    RoomType.SideRoom => "侧室事件！",
                    RoomType.SealPlatform => "封印台！可解救英灵",
                    RoomType.GemShrine => "宝石神龛！可镶嵌/进化",
                    RoomType.Merchant => "游方商！可删卡换属性",
                    RoomType.CashOut => "折现台！可折卡换灵玉",
                    RoomType.Exit => "出口！使用灵锹下楼",
                    RoomType.Boss => "Boss 房！天劫守卫",
                    _ => ""
                };
            }
        }

        /// <summary>
        /// 刷新移动按钮
        /// </summary>
        private void RefreshMoveButtons()
        {
            if (_dungeonVM == null || moveButtonContainer == null) return;

            // 清除旧的
            for (int i = moveButtonContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(moveButtonContainer.GetChild(i).gameObject);
            }

            // 创建新的
            var moves = _dungeonVM.AvailableMoves;
            for (int i = 0; i < moves.Count; i++)
            {
                var room = moves[i];
                var go = CreateMoveButton(room);
                go.transform.SetParent(moveButtonContainer, false);
                go.transform.localPosition = new Vector3(0, -i * 35, 0);
            }
        }

        private GameObject CreateMoveButton(RoomDisplayData room)
        {
            var go = new GameObject($"Move_{room.id}", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(180, 30);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.6f, 0.5f, 0.3f, 0.8f);

            var labelObj = new GameObject("Label", typeof(Text)).GetComponent<RectTransform>();
            labelObj.SetParent(go.transform, false);
            labelObj.anchorMin = Vector2.zero;
            labelObj.anchorMax = Vector2.one;
            labelObj.offsetMin = Vector2.zero;
            labelObj.offsetMax = Vector2.zero;
            var text = labelObj.GetComponent<Text>();
            text.text = $"→ [{room.x},{room.y}] {room.typeName}";
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 12;
            text.color = Color.white;

            var button = go.GetComponent<Button>();
            int capturedId = room.id;
            button.onClick.AddListener(() => _dungeonVM.MoveTo(capturedId));

            return go;
        }

        /// <summary>
        /// 刷新交互按钮
        /// </summary>
        private void RefreshInteractionButtons()
        {
            if (_dungeonVM == null) return;

            var currentRoom = _dungeonVM.CurrentRoom;
            if (!currentRoom.HasValue) return;
            var room = currentRoom.Value;

            bool isCleared = room.cleared;

            // 破坏光源
            if (breakLightButton != null)
                breakLightButton.gameObject.SetActive(room.type == RoomType.Light && !isCleared);

            // 吃灵丹
            if (eatChickenButton != null)
                eatChickenButton.gameObject.SetActive(room.type == RoomType.Chicken && !isCleared);

            // 使用灵锹
            if (useShovelButton != null)
                useShovelButton.gameObject.SetActive(room.type == RoomType.Exit || room.type == RoomType.Boss);
        }
    }
}
