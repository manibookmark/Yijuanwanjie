using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Components;

namespace WanjieScroll.UI.Controllers
{
    /// <summary>
    /// 关卡选择场景控制器
    /// 背景图 + 三个卷轴按钮放在台子上，卷名写在卷轴上
    /// </summary>
    public class LevelSelectController : SceneBootstrapBase
    {
        [Header("可选：Inspector 赋值（留空则自动加载）")]
        [SerializeField] private Sprite _backgroundSprite;

        private static readonly VolumeInfo[] Volumes =
        {
            new VolumeInfo { id = 0, name = "神话卷" },
            new VolumeInfo { id = 1, name = "历史卷" },
            new VolumeInfo { id = 2, name = "修仙卷" }
        };

        protected override void OnSceneEnter()
        {
            CreateBackground();
            CreateScrollButtons();
            UIElementFactory.CreateBackButton(Canvas.transform, () => SceneNavigator.GoToLoading());
        }

        private void CreateBackground()
        {
            Sprite bg = _backgroundSprite != null ? _backgroundSprite : RuntimeArtLoader.GetLevelSelectBackground();
            if (bg != null)
                UIElementFactory.CreateBackground(Canvas.transform, bg, Color.white);
            else
                UIElementFactory.CreateBackground(Canvas.transform, color: new Color(0.12f, 0.1f, 0.15f));
        }

        private void CreateScrollButtons()
        {
            float scrollW = 180f;
            float scrollH = 240f;
            float spacing = 80f;
            float totalW = Volumes.Length * scrollW + (Volumes.Length - 1) * spacing;
            float startX = -totalW / 2f + scrollW / 2f;

            for (int i = 0; i < Volumes.Length; i++)
            {
                CreateScrollButton(Volumes[i], startX + i * (scrollW + spacing), scrollW, scrollH);
            }
        }

        private void CreateScrollButton(VolumeInfo vol, float xPos, float width, float height)
        {
            var btn = UIElementFactory.CreateButton($"Scroll_{vol.name}", Canvas.transform,
                RuntimeArtLoader.GetScrollPanel());

            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.12f);
            rt.anchorMax = new Vector2(0.5f, 0.12f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(xPos, 0);

            if (btn.image.sprite == null)
                btn.image.color = new Color(0.85f, 0.75f, 0.55f, 0.9f);

            // 卷名文字（写在卷轴中央）
            var nameText = UIElementFactory.CreateText("VolumeName", btn.transform, vol.name, 28,
                new Color(0.2f, 0.15f, 0.05f));
            var nameRt = nameText.rectTransform;
            nameRt.anchorMin = new Vector2(0.2f, 0.3f);
            nameRt.anchorMax = new Vector2(0.8f, 0.7f);
            nameRt.offsetMin = Vector2.zero;
            nameRt.offsetMax = Vector2.zero;

            btn.onClick.AddListener(() => OnPortalClicked(vol));

            var hover = btn.gameObject.AddComponent<HoverScaleEffect>();
            hover.HoverScale = 1.08f;
        }

        private void OnPortalClicked(VolumeInfo vol)
        {
            GameSession.Current.SelectedVolumeId = vol.id;
            SceneLoader.GoToRouteSelect();
        }

        private struct VolumeInfo
        {
            public int id;
            public string name;
        }
    }
}
