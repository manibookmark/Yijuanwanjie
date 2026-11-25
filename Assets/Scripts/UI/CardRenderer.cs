using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Cards;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 卡牌 UI 渲染器（程序化构建版）
    /// 从 CardConfig 数据自动构建完整卡牌 UI，无需 Inspector 赋值
    /// 卡牌名称已做在素材里，不需要额外组件
    /// </summary>
    public class CardRenderer : MonoBehaviour
    {
        [Header("UI 引用（程序化创建，无需 Inspector 赋值）")]
        public Image frameImage;           // 卡牌框架
        public Image artImage;            // 卡牌插图
        public Text costText;             // 费用
        public Text descText;            // 描述
        public Image colorOverlay;       // 属性色叠加层

        [Header("状态")]
        public GameObject highlight;
        public GameObject playableIndicator;
        public GameObject comboIndicator;

        private CardConfig _cardData;
        private bool _isPlayable;

        /// <summary>
        /// 绑定卡牌数据并渲染
        /// </summary>
        public void Setup(CardConfig card, bool isPlayable = true)
        {
            _cardData = card;
            _isPlayable = isPlayable;

            // 费用
            if (costText != null)
            {
                costText.text = CardTextRenderer.GetCostText(
                    card.costType,
                    card.GetCost());
            }

            // 描述（card.description 来自 JSON 数据源）
            if (descText != null)
            {
                descText.supportRichText = true;
                CardDisplayData shown = CardDisplayData.FromCard(card);
                if (string.IsNullOrEmpty(shown.description))
                    shown.description = BuildDescription(card);
                descText.text = CardTextRenderer.GetDisplayDescription(shown);
            }

            // 插图
            if (artImage != null)
            {
                Sprite art = RuntimeArtLoader.GetCardIllustration(card.cardName);
                if (art != null)
                {
                    artImage.sprite = art;
                    artImage.preserveAspect = true;
                    artImage.color = Color.white;
                }
                else
                {
                    // 没有插图时用属性色占位
                    artImage.color = CardColorPalette.GetMainColor(card.color) * 0.3f;
                }
            }

            // 属性色叠加
            if (colorOverlay != null)
            {
                Color c = CardColorPalette.GetMainColor(card.color);
                c.a = 0.15f;  // 半透明叠加
                colorOverlay.color = c;
            }

            // 状态
            if (playableIndicator != null) playableIndicator.SetActive(isPlayable);
            if (highlight != null) highlight.SetActive(false);
            if (comboIndicator != null) comboIndicator.SetActive(false);
        }

        /// <summary>
        /// 构建缺少作者文案时的纯文本；颜色与总伤统一交给 CardTextRenderer。
        /// </summary>
        private string BuildDescription(CardConfig card)
        {
            var parts = new System.Collections.Generic.List<string>();

            CardAttackDisplaySummary attackDisplay = CardAttackDisplayProjection.Project(
                card, new CombatStatSnapshot(null, 0));
            if (attackDisplay.MainDamage > 0)
            {
                parts.Add($"{attackDisplay.MainDamage}伤害");
                if (card.canCrit)
                    parts.Add("·暴击");
                if (card.statusEffect != StatusEffect.None)
                {
                    string statusName = card.statusEffect switch
                    {
                        StatusEffect.ZhenTui => "击退",
                        StatusEffect.ZhuoShao => "灼烧",
                        StatusEffect.BingFeng => "冰封",
                        StatusEffect.BaoJi => "暴击",
                        _ => card.statusEffect.ToString()
                    };
                    parts.Add("·" + statusName);
                }
            }

            if (card.armor > 0)
                parts.Add($"+{card.armor}护甲");

            if (card.heal > 0)
                parts.Add($"+{card.heal}气血");

            if (card.manaGain > 0)
                parts.Add($"+{card.manaGain}灵力");

            if (card.destroyOnUse)
                parts.Add("·销毁");

            return string.Join(" ", parts);
        }

        /// <summary>
        /// 更新连击状态显示
        /// </summary>
        public void UpdateComboState(bool inCombo, int multiplier)
        {
            if (comboIndicator != null)
                comboIndicator.SetActive(inCombo);
        }

        /// <summary>
        /// 高亮选中
        /// </summary>
        public void SetHighlight(bool on)
        {
            if (highlight != null)
                highlight.SetActive(on);
        }

        public CardConfig CardData => _cardData;

        // ===== 颜色方案用 CardColorPalette（ArtConfig.cs 中已定义） =====
    }
}
