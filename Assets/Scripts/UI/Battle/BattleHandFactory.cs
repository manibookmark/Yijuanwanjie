using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 创建正式手牌区域。测试和生产场景共享同一层级、计数器与 CardView 配置。
    /// </summary>
    public static class BattleHandFactory
    {
        public static HandView Create(
            Transform parent,
            CardView cardPrefab = null,
            CardPresentationProfile presentation = null)
        {
            presentation ??= CardPresentationProfile.LoadDefault();
            var root = new GameObject(
                "BattleHand",
                typeof(RectTransform),
                typeof(CanvasGroup));
            root.transform.SetParent(parent, false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0f);
            rootRect.anchorMax = new Vector2(0.5f, 0f);
            rootRect.pivot = new Vector2(0.5f, 0f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(1320f, 355f);

            var cardContainerObject = new GameObject(
                "Cards",
                typeof(RectTransform));
            cardContainerObject.transform.SetParent(root.transform, false);
            RectTransform cardContainer =
                cardContainerObject.GetComponent<RectTransform>();
            cardContainer.anchorMin = new Vector2(0.5f, 0f);
            cardContainer.anchorMax = new Vector2(0.5f, 0f);
            cardContainer.pivot = new Vector2(0.5f, 0f);
            cardContainer.anchoredPosition = new Vector2(0f, 10f);
            cardContainer.sizeDelta = new Vector2(1280f, 320f);

            // 创建 HoverLayer（与 Cards 同级，排在后面所以渲染在上层）
            var hoverLayerObj = new GameObject("HoverLayer", typeof(RectTransform));
            hoverLayerObj.transform.SetParent(root.transform, false);
            RectTransform hoverLayer = hoverLayerObj.GetComponent<RectTransform>();
            hoverLayer.anchorMin = new Vector2(0.5f, 0f);
            hoverLayer.anchorMax = new Vector2(0.5f, 0f);
            hoverLayer.pivot = new Vector2(0.5f, 0f);
            hoverLayer.anchoredPosition = new Vector2(0f, 80f);
            hoverLayer.sizeDelta = new Vector2(1280f, 320f);

            HandView hand = root.AddComponent<HandView>();
            hand.cardContainer = cardContainer;
            hand.cardPrefab = cardPrefab;
            hand.cardPresentation = presentation;
            hand.ConfigureLayout(
                angle: 45f,
                radius: 350f,
                scale: 0.75f,
                spacing: 80f,
                maxRot: 15f);

            return hand;
        }

        private static Text CreateCounter(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Text text = go.GetComponent<Text>();
            text.font =
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.84f, 0.45f);
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateContainer(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 size,
            Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }
    }
}
