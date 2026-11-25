using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Meta;

namespace WanjieScroll.UI.Cards
{
    /// <summary>
    /// CardView 印痕层的唯一构建者。普通卡面只画真实印痕；钤印页可额外请求
    /// 空位轮廓和一个只读预览印，不修改 CardConfig 或存档。
    /// </summary>
    public static class CardSealViewRenderer
    {
        private static readonly Color Cinnabar =
            new Color(0.62f, 0.08f, 0.055f, 0.92f);

        public static void Render(
            Transform layer,
            IReadOnlyList<CardSealSpec> seals,
            int slotCapacity,
            bool showEmptySlots,
            int previewSlotIndex = -1,
            CardSealSpec previewSeal = null,
            CardPresentationProfile profile = null)
        {
            if (layer == null)
                return;
            for (int i = layer.childCount - 1; i >= 0; i--)
            {
                GameObject stale = layer.GetChild(i).gameObject;
                stale.SetActive(false);
                Object.Destroy(stale);
            }

            int occupied = seals?.Count ?? 0;
            int visibleSlots = showEmptySlots
                ? Mathf.Clamp(slotCapacity, 0, CardSealRules.MaximumSealSlots)
                : occupied;
            for (int i = 0; i < visibleSlots; i++)
            {
                CardSealSpec seal = i < occupied ? seals[i] : null;
                bool preview = seal == null && i == previewSlotIndex &&
                               previewSeal != null;
                CreateStamp(
                    layer,
                    i,
                    seal ?? (preview ? previewSeal : null),
                    seal == null,
                    preview,
                    profile);
            }
        }

        private static void CreateStamp(
            Transform parent,
            int index,
            CardSealSpec seal,
            bool empty,
            bool preview,
            CardPresentationProfile profile)
        {
            var go = new GameObject(
                $"SealSlot_{index}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            // SealLayer 是 VisualRoot 的直接后代：印自身保持正向，随整张卡
            // 一起旋转缩放。所有印从 Header 下方左侧开始向下纵排。
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            float stampSize = Mathf.Max(1f, profile?.sealSize ?? 44f);
            float insetAndGap = Mathf.Max(
                0f, profile?.sealInsetAndGap ?? 8f);
            rect.sizeDelta = Vector2.one * stampSize;
            rect.anchoredPosition = new Vector2(
                insetAndGap,
                -(insetAndGap + index * (stampSize + insetAndGap)));
            rect.localRotation = Quaternion.identity;

            Image frame = go.GetComponent<Image>();
            frame.sprite = RoundedRectSpriteGenerator.GetOutline(2, 2);
            frame.type = Image.Type.Sliced;
            frame.fillCenter = false;
            Color color = Cinnabar;
            color.a = empty ? (preview ? 0.58f : 0.32f) : 0.92f;
            frame.color = color;
            frame.raycastTarget = false;

            if (seal == null)
                return;
            var textGo = new GameObject(
                "SealCharacter",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            Text text = textGo.GetComponent<Text>();
            text.font = Resources.Load<Font>("Fonts/NotoSerifSC-Medium") ??
                        Resources.Load<Font>("Fonts/NotoSerifSC-Heavy") ??
                        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = GetCharacter(seal);
            text.color = color;
            text.raycastTarget = false;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        }

        private static string GetCharacter(CardSealSpec seal)
        {
            // Formal content owns the stamp, including new effects. The fallback
            // is only for historical builtin specs that have no catalog asset.
            if (LingyinCatalog.TryGet(seal.contentId, out LingyinConfig config) &&
                config.effectType == seal.effect && !string.IsNullOrWhiteSpace(config.sealCharacter))
                return config.sealCharacter;
            return seal.effect switch
            {
                CardSealEffect.DoubleDamage => "双",
                CardSealEffect.TripleDamage => "三",
                CardSealEffect.Luck => "运",
                CardSealEffect.Greed => "玉",
                CardSealEffect.Draw => "引",
                CardSealEffect.Armor => "罡",
                CardSealEffect.Amount => "数",
                CardSealEffect.BengLie => "裂",
                _ => "印"
            };
        }
    }
}
