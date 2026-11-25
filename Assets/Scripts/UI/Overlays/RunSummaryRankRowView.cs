using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Overlays
{
    /// <summary>
    /// 结算页伤害排行单行（卡牌名 / 五行色条形 / 总伤 / 次均伤害）的
    /// Prefab 视图。数值列布局与表头、生成器共用；运行时写入名称与
    /// 条形色，入场动画由 RunSummaryRankingAnimator 直接驱动公开出来的
    /// 条形与数字组件，本视图不做动画。
    /// </summary>
    public sealed class RunSummaryRankRowView : MonoBehaviour
    {
        [SerializeField] private Text nameText;
        [SerializeField] private Image trackImage;
        [SerializeField] private RectTransform fillRect;
        [SerializeField] private Image fillImage;
        [SerializeField] private Text damageText;
        // Retained to hide the old column in existing prefabs without regeneration.
        [SerializeField, HideInInspector] private Text shareText;
        [SerializeField] private Text averageText;

        public RectTransform FillRect => fillRect;
        public Text DamageText => damageText;
        public Text AverageText => averageText;

        public void SetData(string label, Color barColor)
        {
            if (shareText != null)
                shareText.gameObject.SetActive(false);
            ApplyNumericColumnLayout(damageText, averageText);
            if (nameText != null)
                nameText.text = label ?? string.Empty;
            if (fillImage != null)
            {
                fillImage.color = new Color(
                    barColor.r, barColor.g, barColor.b, 0.90f);
            }
        }

        /// <summary>表头、运行时行和 Prefab 生成共用的数值列布局。</summary>
        public static void ApplyNumericColumnLayout(Text damage, Text average)
        {
            PlaceNumericColumn(damage, 0.62f, 0.78f);
            PlaceNumericColumn(average, 0.80f, 0.98f);
        }

        private static void PlaceNumericColumn(Text text, float left, float right)
        {
            if (text == null)
                return;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(left, 0f);
            rect.anchorMax = new Vector2(right, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            text.alignment = TextAnchor.MiddleRight;
            text.fontSize = 16;
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }
    }
}
