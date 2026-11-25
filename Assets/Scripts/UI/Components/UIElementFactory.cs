using System;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Components
{
    /// <summary>
    /// 常见 UGUI 元素的创建工具。消除各 Controller 中重复的
    /// <c>new GameObject(...) + AddComponent + 设锚点</c> 代码。
    /// </summary>
    public static class UIElementFactory
    {
        /// <summary>创建一个全屏拉伸的背景 Image。</summary>
        public static Image CreateBackground(Transform parent, Sprite sprite = null, Color? color = null)
        {
            var go = new GameObject("Background", typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            var rt = go.GetComponent<RectTransform>();
            Stretch(rt);

            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = color ?? Color.white;
            }
            else
            {
                img.color = color ?? new Color(0.08f, 0.06f, 0.05f);
            }

            return img;
        }

        /// <summary>创建一个 Image 元素。</summary>
        public static Image CreateImage(string name, Transform parent, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            if (sprite != null)
            {
                img.sprite = sprite;
                img.preserveAspect = true;
            }
            return img;
        }

        /// <summary>创建一个 Text 元素。</summary>
        public static Text CreateText(
            string name, Transform parent, string text,
            int fontSize, Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);

            var t = go.GetComponent<Text>();
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = alignment;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.raycastTarget = false;
            return t;
        }

        /// <summary>创建一个带 Image 的 Button。</summary>
        public static Button CreateButton(string name, Transform parent, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            if (sprite != null)
            {
                img.sprite = sprite;
                img.preserveAspect = true;
                img.color = Color.white;
            }

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            return btn;
        }

        /// <summary>创建标准返回按钮（左上角图标按钮）。</summary>
        public static Button CreateBackButton(Transform parent, Action onClick)
        {
            var btn = CreateButton("BackButton", parent,
                RuntimeArtLoader.LoadSprite("Sprites/UI/back-button"));

            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(80, 80);
            rt.anchoredPosition = new Vector2(30, -30);

            if (btn.image.sprite == null)
                btn.image.color = new Color(1f, 1f, 1f, 0.5f);

            btn.onClick.AddListener(() => onClick());

            var hover = btn.gameObject.AddComponent<HoverScaleEffect>();
            hover.NormalScale = 1f;
            hover.HoverScale = 1.15f;

            return btn;
        }

        /// <summary>将 RectTransform 拉伸到全屏（anchor 0,0 → 1,1）。</summary>
        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
