using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Content;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.UI;
using WanjieScroll.UI.Components;
using WanjieScroll.UI.Daozang;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Controllers
{
    /// <summary>
    /// 地牢探索场景控制器
    /// 程序化构建地牢探索 UI，接入 DungeonGenerator → ExplorationController → DungeonViewModel
    /// </summary>
    public class DungeonSceneController : SceneBootstrapBase
    {
        private ExplorationController _exploration;
        private DungeonViewModel _dungeonVM;
        private DungeonMapView _mapView;
        private ExplorationHUD _hudView;

        protected override void OnSceneEnter()
        {
            CreateBackground();

            // === 生成地牢 ===
            string volumeId = GameSession.Current.GetVolumeId();
            VolumeContent content = ContentCatalog.Current.GetVolume(volumeId);
            var config = CreateDungeonConfig(content.dungeons);
            var floor = DungeonGenerator.GenerateFloor(config, 1, config.hasBoss);

            var meta = new MetaProgression();
            GameSaveService save = GameSaveService.CreateDefault();
            if (!save.TryLoad(meta, out _))
                meta.lingyu = GameSession.Current.PlayerLingyu;
            var economyStats = new PlayerStats();
            DaozangConfig.ApplyToPlayerStats(meta, economyStats);
            RunProgressionState runState = GameSession.Current.RunProgression;
            var economy = new EconomySystem(new EconomyConfig(), runState, amount =>
            {
                meta.CreditLingyu(amount);
                GameSession.Current.PlayerLingyu = meta.lingyu;
                save.Save(meta);
            });
            _exploration = new ExplorationController(
                floor,
                120,
                120,
                economy: economy,
                getGreed: () => runState.ProjectGreed(economyStats.lianYu));
            _exploration.OnEvent += OnExplorationEvent;

            // === ViewModel ===
            _dungeonVM = new DungeonViewModel();
            _dungeonVM.Bind(_exploration);

            CreateMapArea();
            CreateHUDArea();

            UIElementFactory.CreateBackButton(Canvas.transform, () => SceneLoader.GoToRouteSelect());

            Debug.Log("[DungeonScene] 地牢探索场景构建完成");
        }

        private void CreateBackground()
        {
            Sprite bg = RuntimeArtLoader.GetScrollSelectBackground();
            if (bg != null)
                UIElementFactory.CreateBackground(Canvas.transform, bg, new Color(0.15f, 0.12f, 0.1f, 0.85f));
            else
                UIElementFactory.CreateBackground(Canvas.transform, color: new Color(0.06f, 0.05f, 0.04f));
        }

        private void CreateMapArea()
        {
            // 地图区域（左侧 65%）
            var mapArea = CreatePanel("MapArea", new Vector2(0, 0), new Vector2(0.65f, 1));
            var mapContainerGo = new GameObject("MapContainer", typeof(RectTransform));
            mapContainerGo.transform.SetParent(mapArea, false);
            var mapContainerRt = mapContainerGo.GetComponent<RectTransform>();
            mapContainerRt.anchorMin = new Vector2(0.5f, 0.5f);
            mapContainerRt.anchorMax = new Vector2(0.5f, 0.5f);
            mapContainerRt.sizeDelta = new Vector2(800, 800);
            mapContainerRt.anchoredPosition = Vector2.zero;

            _mapView = mapArea.gameObject.AddComponent<DungeonMapView>();
            _mapView.mapContainer = mapContainerRt;
            _mapView.cellSize = 60f;
            _mapView.cellSpacing = 5f;
            _mapView.Bind(_dungeonVM);
        }

        private void CreateHUDArea()
        {
            // HUD 区域（右侧 35%）
            var hudArea = CreatePanel("HUDArea", new Vector2(0.65f, 0), new Vector2(1, 1));

            // HUD 背景
            var hudBg = UIElementFactory.CreateImage("HUDBg", hudArea);
            UIElementFactory.Stretch(hudBg.rectTransform);
            hudBg.color = new Color(0.08f, 0.06f, 0.05f, 0.7f);

            // 文字
            var progressText = CreateHudText(hudArea, "ProgressText", "", 18, new Color(0.9f, 0.85f, 0.7f),
                new Vector2(0.05f, 0.92f), new Vector2(0.95f, 0.98f));
            var hpText = CreateHudText(hudArea, "HPText", "", 16, new Color(0.8f, 0.3f, 0.3f),
                new Vector2(0.05f, 0.85f), new Vector2(0.95f, 0.92f));
            var lingyuText = CreateHudText(hudArea, "LingyuText", "", 22, new Color(0.8f, 0.9f, 0.72f),
                new Vector2(0.16f, 0.77f), new Vector2(0.95f, 0.87f));
            var lingyuIcon = UIElementFactory.CreateImage("LingyuIcon", hudArea);
            lingyuIcon.sprite = Resources.Load<Sprite>("Sprites/Maps/jade-node");
            lingyuIcon.preserveAspect = true;
            lingyuIcon.raycastTarget = false;
            var lingyuIconRt = lingyuIcon.rectTransform;
            lingyuIconRt.anchorMin = new Vector2(0.05f, 0.77f);
            lingyuIconRt.anchorMax = new Vector2(0.15f, 0.87f);
            lingyuIconRt.offsetMin = Vector2.zero;
            lingyuIconRt.offsetMax = Vector2.zero;
            var currentRoomText = CreateHudText(hudArea, "CurrentRoomText", "", 20, new Color(0.95f, 0.9f, 0.75f),
                new Vector2(0.05f, 0.65f), new Vector2(0.95f, 0.75f));
            var roomDescText = CreateHudText(hudArea, "RoomDescText", "", 14, new Color(0.7f, 0.65f, 0.55f),
                new Vector2(0.05f, 0.55f), new Vector2(0.95f, 0.65f));

            // 交互按钮容器
            var interactionContainer = new GameObject("InteractionContainer", typeof(RectTransform));
            interactionContainer.transform.SetParent(hudArea, false);
            var interactionRt = interactionContainer.GetComponent<RectTransform>();
            interactionRt.anchorMin = new Vector2(0.05f, 0.35f);
            interactionRt.anchorMax = new Vector2(0.95f, 0.52f);
            interactionRt.offsetMin = Vector2.zero;
            interactionRt.offsetMax = Vector2.zero;

            var breakLightBtn = CreateHudButton(interactionContainer.transform, "BreakLightBtn", "破坏光源", 0);
            var eatChickenBtn = CreateHudButton(interactionContainer.transform, "EatChickenBtn", "食用灵丹", 1);
            var useShovelBtn = CreateHudButton(interactionContainer.transform, "UseShovelBtn", "使用灵锹", 2);

            // 移动按钮容器
            var moveContainerGo = new GameObject("MoveContainer", typeof(RectTransform));
            moveContainerGo.transform.SetParent(hudArea, false);
            var moveContainerRt = moveContainerGo.GetComponent<RectTransform>();
            moveContainerRt.anchorMin = new Vector2(0.05f, 0.05f);
            moveContainerRt.anchorMax = new Vector2(0.95f, 0.32f);
            moveContainerRt.offsetMin = Vector2.zero;
            moveContainerRt.offsetMax = Vector2.zero;

            // HUD 组件
            _hudView = hudArea.gameObject.AddComponent<ExplorationHUD>();
            _hudView.progressText = progressText;
            _hudView.hpText = hpText;
            _hudView.lingyuText = lingyuText;
            _hudView.lingyuIcon = lingyuIcon;
            _hudView.currentRoomText = currentRoomText;
            _hudView.roomDescriptionText = roomDescText;
            _hudView.breakLightButton = breakLightBtn;
            _hudView.eatChickenButton = eatChickenBtn;
            _hudView.useShovelButton = useShovelBtn;
            _hudView.moveButtonContainer = moveContainerRt;
            _hudView.moveButtonPrefab = null;
            _hudView.Bind(_dungeonVM);
        }

        private RectTransform CreatePanel(string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(Canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Text CreateHudText(Transform parent, string name, string text, int fontSize, Color color,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var t = UIElementFactory.CreateText(name, parent, text, fontSize, color);
            var rt = t.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return t;
        }

        private static Button CreateHudButton(Transform parent, string name, string label, int index)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1 - (index + 1) * 0.34f);
            rt.anchorMax = new Vector2(1, 1 - index * 0.34f);
            rt.offsetMin = new Vector2(5, 3);
            rt.offsetMax = new Vector2(-5, -3);

            go.GetComponent<Image>().color = new Color(0.2f, 0.15f, 0.1f, 0.9f);

            var labelText = UIElementFactory.CreateText("Label", go.transform, label, 16,
                new Color(0.9f, 0.85f, 0.7f));
            UIElementFactory.Stretch(labelText.rectTransform);

            go.SetActive(false);
            return go.GetComponent<Button>();
        }

        private DungeonConfig CreateDungeonConfig(List<DungeonData> dungeons)
        {
            int idx = Mathf.Clamp(GameSession.Current.SelectedDungeonIndex, 0, dungeons.Count - 1);
            var data = dungeons[idx];
            Debug.Log($"[DungeonScene] 地牢: {data.name}, 难度{data.difficulty}, {data.floors}层, Boss={data.bossName}");
            return new DungeonConfig
            {
                dungeonName = data.name,
                volumeId = data.volumeId,
                totalFloors = data.floors,
                difficulty = data.difficulty,
                roomsPerFloor = 8,
                hasBoss = data.hasBoss,
            };
        }

        private void OnExplorationEvent(ExplorationEvent explorationEvent)
        {
            if (explorationEvent.Type == ExplorationEventType.TreasureOpened)
                _hudView?.PlayJadeGain(explorationEvent.Amount);
            if (explorationEvent.Type == ExplorationEventType.EncounterStarted
                || explorationEvent.Type == ExplorationEventType.BossEncountered)
            {
                SceneLoader.GoToCombat();
            }
        }

        protected override void OnSceneExit()
        {
            if (_exploration != null)
                _exploration.OnEvent -= OnExplorationEvent;
            _dungeonVM?.Dispose();
        }
    }
}
