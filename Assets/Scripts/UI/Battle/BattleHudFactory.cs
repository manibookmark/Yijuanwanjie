using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 战斗 HUD 装配入口。
    /// 布局来源唯一：BattleHud.prefab。
    /// 工厂只负责实例化 prefab + 注入表现配置，不从零构建 UI 层级。
    /// 静态工具方法保留给需要运行时动态生成的组件（如 BuffBar 图标）使用。
    /// </summary>
    public static class BattleHudFactory
    {
        public const string ResourcePath = "UI/Battle/BattleHud";

        public static BattleHudView Create(
            Transform parent,
            BattleHudPresentationProfile profile = null)
        {
            profile ??= BattleHudPresentationProfile.LoadDefault();

            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"[BattleHudFactory] BattleHud prefab not found in Resources: {ResourcePath}");
                return null;
            }

            var go = Object.Instantiate(prefab, parent, false);
            var view = go.GetComponent<BattleHudView>();
            if (view == null)
            {
                Debug.LogError("[BattleHudFactory] BattleHudView component not found on prefab");
                return null;
            }

            view.ApplyPresentation(profile);
            view.armorShieldView?.Initialize(view.hpFullImage, view.hpText);
            return view;
        }

        // ── 静态工具方法（供动态生成的组件使用，不用于 BattleHud 层级构建）──

        public static RectTransform CreateContainer(
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

        public static Image CreateImage(Transform parent, string name)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.clear;
            image.preserveAspect = true;
            return image;
        }

        public static Text CreateText(
            Transform parent,
            string name,
            TextAnchor alignment)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        public static void ApplyRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static void Place(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
