using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 卡牌布局预览 Bootstrap。
    /// 使用四张代表卡的独立插图和七种类型配色进行预览。
    /// </summary>
    public class CardLayoutPreviewBootstrap : MonoBehaviour
    {
        public CardPresentationProfile presentation;
        public CardColor previewColor = CardColor.Huo;
        public int previewCost = 3;
        public string previewDescription = "40伤害<color=#FF0000>·暴击</color>";

        private static readonly (CardColor color, string contentId, string name)[] SampleCards =
        {
            (CardColor.Huo,  "legacy.card.common.7e46b7e4",         "飞刀"),
            (CardColor.Huo,  "legacy.card.xiyou.0578cfde",          "芭蕉扇"),
            (CardColor.Huo,  "legacy.card.common.5aa5940d",         "神火飞鸦"),
            (CardColor.Huo,  "legacy.card.common.f22c29d6",         "开山斧"),
            (CardColor.Tu,   "legacy.card.common.2927e58a",         "锁子甲"),
            (CardColor.Mu,   "legacy.card.common.973d06a5",         "通脉草"),
            (CardColor.Jin,  "legacy.crawler.common.5a3f3db3",       "孙悟空"),
            (CardColor.Yang, "legacy.card.xiyou.315dc4d8",           "筋斗云"),
            // 水和阴暂时用飞刀插图配不同类型色
            (CardColor.Shui, "legacy.card.common.7e46b7e4",         "飞刀"),
            (CardColor.Yin,  "legacy.card.common.7e46b7e4",         "飞刀"),
        };

        private CardView _cardView;
        public CardView CardView => _cardView;

        private void Start() { Build(); }

        public void Build()
        {
            if (_cardView != null) return;
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
            CardPresentationProfile profile = presentation != null ? presentation : CardPresentationProfile.LoadDefault();
            _cardView = CardViewFactory.Create(canvas.transform, profile);
            var rt = _cardView.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(400f, 600f); rt.localScale = Vector3.one;
            RefreshView();
        }

        public void RefreshView()
        {
            if (_cardView == null) return;
            CardPresentationProfile profile = presentation != null ? presentation : CardPresentationProfile.LoadDefault();

            string contentId = "legacy.card.common.7e46b7e4";
            string cardName = "飞刀";
            foreach (var s in SampleCards)
                if (s.color == previewColor) { contentId = s.contentId; cardName = s.name; break; }

            var data = new CardDisplayData
            {
                contentId = contentId,
                name = cardName,
                description = previewDescription,
                color = previewColor,
                cost = previewCost,
                costType = CostType.Default,
                canPlay = true
            };
            _cardView.SetPresentationProfile(profile);
            _cardView.UpdateCard(data, 0);
            var rt = _cardView.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400f, 600f);
        }
    }
}
