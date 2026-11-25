using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Content;
using WanjieScroll.UI.Components;

namespace WanjieScroll.UI.Controllers
{
    /// <summary>
    /// 路线选择场景控制器
    /// 显示当前卷的地牢列表，点击进入地牢探索
    /// </summary>
    public class RouteSelectController : SceneBootstrapBase
    {
        protected override void OnSceneEnter()
        {
            CreateBackground();

            string volumeId = GameSession.Current.GetVolumeId();
            VolumeContent content = ContentCatalog.Current.GetVolume(volumeId);
            List<DungeonData> dungeons = content.dungeons;

            CreateDungeonCards(dungeons);

            UIElementFactory.CreateBackButton(Canvas.transform, () => SceneNavigator.GoToLevelSelect());
        }

        private void CreateBackground()
        {
            Sprite bg = RuntimeArtLoader.GetScrollSelectBackground();
            if (bg != null)
                UIElementFactory.CreateBackground(Canvas.transform, bg, new Color(0.3f, 0.25f, 0.2f, 0.85f));
            else
                UIElementFactory.CreateBackground(Canvas.transform, color: new Color(0.08f, 0.06f, 0.05f));
        }

        private void CreateDungeonCards(List<DungeonData> dungeons)
        {
            float cardWidth = 400f;
            float cardHeight = 200f;
            float spacing = 40f;
            float totalWidth = dungeons.Count * cardWidth + (dungeons.Count - 1) * spacing;
            float startX = -totalWidth / 2f + cardWidth / 2f;

            for (int i = 0; i < dungeons.Count; i++)
            {
                CreateDungeonCard(dungeons[i], i, startX + i * (cardWidth + spacing), cardWidth, cardHeight);
            }
        }

        private void CreateDungeonCard(DungeonData dungeon, int index, float xPos, float width, float height)
        {
            var btn = UIElementFactory.CreateButton($"Dungeon_{dungeon.name}", Canvas.transform,
                RuntimeArtLoader.GetScrollPanel());

            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.45f);
            rt.anchorMax = new Vector2(0.5f, 0.45f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(xPos, 0);

            btn.image.type = Image.Type.Sliced;
            btn.image.color = new Color(0.15f, 0.12f, 0.1f, 0.9f);

            // 地牢名
            var nameText = UIElementFactory.CreateText("Name", btn.transform, dungeon.name, 28,
                new Color(0.95f, 0.85f, 0.3f));
            SetAnchors(nameText.rectTransform, new Vector2(0, 0.6f), new Vector2(1, 1f));

            // 难度
            var diffText = UIElementFactory.CreateText("Difficulty", btn.transform,
                $"难度 {dungeon.difficulty}  ·  {dungeon.floors} 层", 18, new Color(0.8f, 0.75f, 0.6f));
            SetAnchors(diffText.rectTransform, new Vector2(0, 0.35f), new Vector2(1, 0.6f));

            // Boss
            if (dungeon.hasBoss)
            {
                var bossText = UIElementFactory.CreateText("Boss", btn.transform,
                    $"Boss: {dungeon.bossName}", 16, new Color(0.7f, 0.3f, 0.25f));
                SetAnchors(bossText.rectTransform, new Vector2(0, 0.1f), new Vector2(1, 0.35f));
            }

            // 奖励
            if (!string.IsNullOrEmpty(dungeon.reward) && dungeon.reward != "无")
            {
                var rewardText = UIElementFactory.CreateText("Reward", btn.transform,
                    $"奖励: {dungeon.reward}", 14, new Color(0.6f, 0.8f, 0.6f));
                SetAnchors(rewardText.rectTransform, new Vector2(0, -0.05f), new Vector2(1, 0.1f));
            }

            int capturedIndex = index;
            btn.onClick.AddListener(() => OnDungeonClicked(capturedIndex));

            var hover = btn.gameObject.AddComponent<HoverScaleEffect>();
            hover.HoverScale = 1.05f;
        }

        private static void SetAnchors(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void OnDungeonClicked(int dungeonIndex)
        {
            GameSession.Current.StartNewRun();
            GameSession.Current.SelectedDungeonIndex = dungeonIndex;
            SceneLoader.GoToDungeon();
        }
    }
}
