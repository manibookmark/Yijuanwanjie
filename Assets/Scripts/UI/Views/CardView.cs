using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Cards;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 卡牌视图（单张卡牌渲染）
    /// 程序化分层组合：插图 + 类型色 + 纸纹 + 费用框 + 文字 + 边线 + 灵印。
    /// 本视图唯一负责单卡样式组合；费用受扰边框的对象和显示通道由独立
    /// CardCostConfusionPresentation持有，本视图只交付快照及生命周期。
    /// </summary>
    public class CardView : UIView
    {
        [Header("UI 元素")]
        public Image cardArtImage;
        public Image borderImage;
        public Image iconImage;
        public Text nameText;
        public Text costText;
        public Text descriptionText;
        public Text colorTagText;
        public Image durabilityBar;

        [Header("程序化层级")]
        public RectTransform visualRoot; // 视觉根节点（旋转缩放挂在这里）
        public Image backgroundImage;
        public Image paperFiberImage;
        public Image typeHeaderImage;
        public Image headerInkWashImage;
        public Image costFrameImage;
        public Image costBorderImage;
        public Image thinBorderImage;
        public Image disabledOverlay;
        public Image hoverOrSelectedOutline;
        public Image glowScatterImage;   // 外散光（比卡牌大，低alpha）
        public Image glowProxyImage;       // 边缘发光（rim glow）
        public Transform sealLayerTransform;

        [Header("交互")]
        public Button cardButton;
        public GameObject hoverHighlight;

        [Header("表现资产")]
        [SerializeField] private CardPresentationProfile presentationProfile;

        [Header("免耗图标")]
        [Tooltip("FreeToPlay 卡牌显示的免字图标，放在费用文字右边")]
        public Image freeIconImage;

        private CardDisplayData _cardData;
        private int _sortedIndex = -1;
        private bool _isSelected;
        private bool _canAfford = true;
        private bool _comboPreviewActive;
        private string _originalDescription;
        private bool _showEmptySealSlots;
        private int _previewSealSlotIndex = -1;
        private CardSealSpec _previewSeal;
        private WildCardAmbientGlowGraphic _wildHandGlow;
        private CardCostConfusionPresentation _costConfusion;

        /// <summary>卡片数据</summary>
        public CardDisplayData CardData => _cardData;
        public int SortedIndex => _sortedIndex;

        protected override void OnViewModelChanged(string propertyName)
        {
        }
        /// <summary>
        /// 编辑器模式下也运行，确保 SDF Material 和 Rounded Sprite 在场景加载后重建。
        /// 这些运行时对象不会序列化进 Prefab 或场景文件。
        /// </summary>
        protected virtual void OnEnable()
        {
            // 无论编辑器还是 Play 模式，都重建 SDF Material 和 Rounded Sprite
            // 这些运行时对象不会序列化进 Prefab 或场景文件
            var profile = presentationProfile != null
                ? presentationProfile
                : CardPresentationProfile.LoadDefault();
            if (profile != null && _cardData.color != default)
                ApplyPresentation(_cardData.color);
            else if (profile != null)
                ApplyPresentation(CardColor.Huo);
            ApplyAffordabilityVisual();
            RefreshCostConfusion();
        }

        protected virtual void OnDisable()
        {
            _costConfusion?.Hide();
        }

        protected override void OnDestroy()
        {
            _costConfusion?.Dispose();
            _costConfusion = null;
            base.OnDestroy();
        }

        /// <summary>
        /// 运行时重建 Mask 和 ThinBorder 的圆角 Sprite。
        /// 这些 Sprite 由 RoundedRectSpriteGenerator 动态生成，无法序列化进 Prefab。
        /// 同时确保 SDF 材质存在。
        /// </summary>
        private void EnsureRoundedSprites()
        {
            CardPresentationProfile profile =
                presentationProfile != null
                    ? presentationProfile
                    : CardPresentationProfile.LoadDefault();
            if (profile == null) return;

            int radius = Mathf.RoundToInt(profile.cornerRadius);
            int bw = Mathf.RoundToInt(profile.borderWidth);

            // ThinBorder: 9-slice 圆角描边
            if (thinBorderImage != null)
            {
                thinBorderImage.sprite = RoundedRectSpriteGenerator.GetOutline(radius, bw);
                thinBorderImage.type = Image.Type.Sliced;
                thinBorderImage.fillCenter = false;
            }

            // HoverOrSelectedOutline
            if (hoverOrSelectedOutline != null)
            {
                hoverOrSelectedOutline.sprite = RoundedRectSpriteGenerator.GetOutline(radius, 2);
                hoverOrSelectedOutline.type = Image.Type.Sliced;
                hoverOrSelectedOutline.fillCenter = false;
            }
        }

        /// <summary>
        /// 确保 CardBackground 不使用特殊材质 — 纯普通 Image。
        /// 插图已预烘焙（背景色+纸纹融合），不需要运行时 shader。
        /// </summary>
        private void EnsureSDFMaterial(CardPresentationProfile profile)
        {
            // 不再需要 SDF material — 简化方案
            if (backgroundImage != null)
                backgroundImage.material = null;
        }

        public void UpdateCard(CardDisplayData data, int sortedIndex)
        {
            // Pooled views can be refreshed without a matching pointer-exit
            // (for example after a card is played while hovered). Never let
            // the previous card's combo description suppress this card's base
            // description.
            ResetComboPreview();
            _cardData = data;
            _sortedIndex = sortedIndex;
            UpdateWildHandGlow(
                data.costType == CostType.Wild &&
                sortedIndex >= 0 &&
                GetComponent<CardHoverHandler>() != null);
            ApplyPresentation(data.color);

            // 判断表现模式
            CardPresentationProfile.ArtworkBinding binding =
                presentationProfile?.GetBinding(data.contentId);
            bool isLegacy = binding != null &&
                binding.presentationMode ==
                    CardPresentationProfile.PresentationMode.LegacyFullCard;

            if (isLegacy)
                ApplyLegacyMode();
            else
                ApplyProgrammaticMode(data, binding);

            // 卡名
            if (nameText != null)
                nameText.text = data.name;

            // 费用
            if (costText != null)
                costText.text = CardTextRenderer.GetCostText(data.costType, data.cost);

            // 免耗图标：仅 FreeToPlay 卡牌显示
            if (freeIconImage != null)
                freeIconImage.gameObject.SetActive(data.isFreeToPlay);

            // 描述（含锋锐加成的动态数值；连击预览激活时不覆盖）
            if (descriptionText != null && !_comboPreviewActive)
                descriptionText.text = CardTextRenderer.GetDisplayDescription(data);

            RenderSeals();
            RefreshCostConfusion();

            // 类型色
            CardPresentationProfile profile =
                presentationProfile != null
                    ? presentationProfile
                    : CardPresentationProfile.LoadDefault();
            if (profile == null) return;

            CardPresentationProfile.ColorStyle style =
                profile.GetStyle(data.color);

            // 程序化模式下设置颜色
            if (!isLegacy)
            {
                if (typeHeaderImage != null)
                    typeHeaderImage.color = style.headerColor;
                if (costBorderImage != null)
                    costBorderImage.color = style.costBorderColor;
                if (thinBorderImage != null)
                    thinBorderImage.color = style.borderColor;
                // ThinBorder: 直接设置 Image.color 为类型色
                if (thinBorderImage != null)
                    thinBorderImage.color = style.borderColor;

                // 文字颜色：名称和费用按类型，描述固定深墨
                if (nameText != null)
                    nameText.color = style.headerTextColor;
                if (costText != null)
                    costText.color = style.headerTextColor;
                if (descriptionText != null)
                    descriptionText.color = profile.bodyTextColor;

                // 深色卡头才加阴影；金、阳和描述不加
                bool isLightHeader = data.color == CardColor.Jin || data.color == CardColor.Yang;
                EnsureShadow(nameText, !isLightHeader);
                EnsureShadow(costText, !isLightHeader);
                EnsureShadow(descriptionText, false);
            }

            if (cardButton != null)
                cardButton.interactable = data.canPlay;

            SetAffordability(data.canPlay);
        }

        private void RefreshCostConfusion()
        {
            bool visible = isActiveAndEnabled && _cardData.hasConfusedCostChange;
            if (!visible)
            {
                _costConfusion?.Hide();
                return;
            }

            CardPresentationProfile profile = presentationProfile != null
                ? presentationProfile : CardPresentationProfile.LoadDefault();
            _costConfusion ??= new CardCostConfusionPresentation();
            _costConfusion.Show(thinBorderImage != null
                    ? thinBorderImage.rectTransform : visualRoot,
                Mathf.RoundToInt(profile?.cornerRadius ?? 0f));
        }

        /// <summary>
        /// 手牌中的 Wild 牌复用一层独立的低频沿边灵光。奖励预览、牌堆列表
        /// 等同样使用 CardView 的位置没有 CardHoverHandler，因而不会误开常驻效果。
        /// </summary>
        private void UpdateWildHandGlow(bool visible)
        {
            if (!visible)
            {
                _wildHandGlow?.SetAmbientVisible(false);
                return;
            }

            if (_wildHandGlow == null)
            {
                Transform insertBefore = costFrameImage != null
                    ? costFrameImage.transform
                    : thinBorderImage != null
                        ? thinBorderImage.transform
                        : null;
                _wildHandGlow = WildCardAmbientGlowGraphic.Ensure(
                    visualRoot,
                    insertBefore);
            }

            if (_wildHandGlow == null) return;

            CardPresentationProfile profile = presentationProfile != null
                ? presentationProfile
                : CardPresentationProfile.LoadDefault();
            _wildHandGlow.Configure(profile?.cornerRadius ?? 12f);
            _wildHandGlow.SetAmbientVisible(true);
        }

        /// <summary>设置仅用于钤印页的空印位与待钤预览。</summary>
        public void SetSealAssignmentPreview(
            bool showEmptySlots,
            int previewSlotIndex = -1,
            CardSealSpec previewSeal = null)
        {
            _showEmptySealSlots = showEmptySlots;
            _previewSealSlotIndex = previewSlotIndex;
            _previewSeal = previewSeal?.Clone();
            RenderSeals();
        }

        private void RenderSeals()
        {
            CardSealViewRenderer.Render(
                sealLayerTransform,
                _cardData.seals,
                _cardData.sealSlotCapacity,
                _showEmptySealSlots,
                _previewSealSlotIndex,
                _previewSeal,
                presentationProfile != null
                    ? presentationProfile
                    : CardPresentationProfile.LoadDefault());
        }

        /// <summary>
        /// hover 时显示连击增益后的数值。
        /// 统一收集所有可缩放数值效果（legacy 字段 + spec effects），逐个在描述中替换。
        /// </summary>
        public void ApplyComboPreview(int combo, WanjieScroll.Core.PlayerStats player)
        {
            string previewDesc = CardTextRenderer.GetComboPreviewDescription(_cardData, combo);
            if (previewDesc == (_cardData.description ?? ""))
            {
                ResetComboPreview();
                return;
            }

            _comboPreviewActive = true;
            _originalDescription = descriptionText != null ? descriptionText.text : null;

            if (descriptionText != null)
                descriptionText.text = previewDesc;
        }

        /// <summary>
        /// 退出 hover 时恢复基础数值。
        /// </summary>
        public void ResetComboPreview()
        {
            if (!_comboPreviewActive) return;
            _comboPreviewActive = false;

            if (descriptionText != null && _originalDescription != null)
                descriptionText.text = _originalDescription;
        }

        private void ApplyProgrammaticMode(
            CardDisplayData data,
            CardPresentationProfile.ArtworkBinding binding)
        {
            // 显示程序化层
            SetProgrammaticLayersActive(true);

            // 插图
            if (cardArtImage != null)
            {
                Sprite artwork = binding?.artwork;
                if (artwork == null && presentationProfile != null)
                    artwork = presentationProfile.GetArtwork(data.contentId);

                if (artwork != null)
                {
                    cardArtImage.sprite = artwork;
                    cardArtImage.color = Color.white;
                    cardArtImage.preserveAspect = true; // 文档：等比 Fit，不拉伸
                }
                else
                {
                    var artConfig = UIController.ArtConfig;
                    if (artConfig != null && artConfig.defaultCardArt != null)
                    {
                        cardArtImage.sprite = artConfig.defaultCardArt;
                        cardArtImage.color = Color.white;
                    }
                    else
                    {
                        cardArtImage.sprite = null;
                        cardArtImage.color = Color.clear;
                    }
                }
            }
        }

        private void ApplyLegacyMode()
        {
            // 隐藏所有程序化 UI 层，只保留旧完整卡面
            SetProgrammaticLayersActive(false);
        }

        private void SetProgrammaticLayersActive(bool active)
        {
            if (backgroundImage != null) backgroundImage.gameObject.SetActive(active);
            if (paperFiberImage != null) paperFiberImage.gameObject.SetActive(active);
            if (typeHeaderImage != null) typeHeaderImage.gameObject.SetActive(active);
            if (costFrameImage != null) costFrameImage.gameObject.SetActive(active);
            if (thinBorderImage != null) thinBorderImage.gameObject.SetActive(active);
            if (nameText != null) nameText.gameObject.SetActive(active);
            if (descriptionText != null) descriptionText.gameObject.SetActive(active);
            if (sealLayerTransform != null) sealLayerTransform.gameObject.SetActive(active);
        }

        /// <summary>
        /// 更新灵力支付状态。费用不足只弱化费用文字；旧遮罩节点始终关闭。
        /// </summary>
        public void SetAffordability(bool canAfford)
        {
            _canAfford = canAfford;
            ApplyAffordabilityVisual();

            if (!canAfford)
            {
                SetHighlight(false);
                UpdateOutline();
                return;
            }

            CardHoverHandler hover = GetComponent<CardHoverHandler>();
            if (hover != null && hover.IsHovering &&
                (cardButton == null || cardButton.interactable))
            {
                SetHighlight(true);
            }
            else
            {
                UpdateOutline();
            }
        }

        private void ApplyAffordabilityVisual()
        {
            // 旧节点只为已有 Prefab/场景序列化引用保留，不再承担状态表现。
            if (disabledOverlay != null)
                disabledOverlay.gameObject.SetActive(false);

            if (costText == null) return;

            CardPresentationProfile profile =
                presentationProfile != null
                    ? presentationProfile
                    : CardPresentationProfile.LoadDefault();
            CardPresentationProfile.ColorStyle style =
                profile?.GetStyle(_cardData.color);
            Color normalColor = style?.headerTextColor ?? Color.white;
            Color insufficientColor = profile?.insufficientCostColor
                ?? CardPresentationProfile.HexColor("#8A8177");
            costText.color = _canAfford ? normalColor : insufficientColor;
        }

        public void SetPresentationProfile(CardPresentationProfile profile)
        {
            presentationProfile = profile != null
                ? profile
                : CardPresentationProfile.LoadDefault();
            ApplyPresentation(_cardData.color);
            ApplyAffordabilityVisual();
            RefreshCostConfusion();
        }

        public void SetArtOverride(Sprite sprite)
        {
            if (cardArtImage == null) return;
            cardArtImage.sprite = sprite;
            cardArtImage.color = sprite != null ? Color.white : cardArtImage.color;
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            UpdateOutline();
        }

        private void UpdateOutline()
        {
            if (hoverOrSelectedOutline == null) return;

            CardPresentationProfile profile =
                presentationProfile != null
                    ? presentationProfile
                    : CardPresentationProfile.LoadDefault();

            // Hover 时由 CardHoverHandler 控制 outline
            var hover = GetComponent<CardHoverHandler>();
            if (hover != null && hover.IsHovering) return;

            if (_isSelected && cardButton != null && cardButton.interactable)
            {
                hoverOrSelectedOutline.color = profile?.selectedOutlineColor
                    ?? new Color(0.847f, 0.718f, 0.408f, 0.851f);
                hoverOrSelectedOutline.gameObject.SetActive(true);
            }
            else
            {
                hoverOrSelectedOutline.gameObject.SetActive(false);
            }
        }

        private void ApplyPresentation(CardColor color)
        {
            CardPresentationProfile profile =
                presentationProfile != null
                    ? presentationProfile
                    : CardPresentationProfile.LoadDefault();
            if (profile == null) return;

            presentationProfile = profile;
            EnsureRoundedSprites();

            var root = transform as RectTransform;
            if (root != null)
                root.sizeDelta = profile.referenceSize;

            profile.artworkRect.Apply(cardArtImage?.rectTransform);
            profile.typeHeaderRect.Apply(typeHeaderImage?.rectTransform);
            profile.costFrameRect.Apply(costFrameImage?.rectTransform);
            profile.nameRect.Apply(nameText?.rectTransform);
            profile.descriptionRect.Apply(descriptionText?.rectTransform);
            profile.sealLayerRect.Apply(sealLayerTransform as RectTransform);

            if (backgroundImage != null)
                backgroundImage.color = profile.cardBaseColor;

            if (paperFiberImage != null)
            {
                paperFiberImage.sprite = profile.paperFiberSprite;
                paperFiberImage.color = new Color(1f, 1f, 1f, profile.paperFiberAlpha);
            }

            CardPresentationProfile.ColorStyle style = profile.GetStyle(color);

            if (headerInkWashImage != null)
            {
                // 兼容旧场景的序列化节点；所有卡名下方不再叠加云状晕墨。
                headerInkWashImage.gameObject.SetActive(false);
            }

            if (typeHeaderImage != null)
                typeHeaderImage.color = style.headerColor;
            if (costBorderImage != null)
                costBorderImage.color = style.costBorderColor;
            if (thinBorderImage != null)
                thinBorderImage.color = style.borderColor;

            // 字体
            ApplyFont(nameText, profile.titleFont, profile.nameFontSize, style.headerTextColor);
            ApplyFont(costText, profile.numberFont, profile.costFontSize, style.headerTextColor);
            ApplyFont(descriptionText, profile.bodyFont, profile.descriptionFontSize, profile.bodyTextColor);

            if (colorTagText != null)
                colorTagText.gameObject.SetActive(false);

            var hover = GetComponent<CardHoverHandler>();
            hover?.ApplyPresentation(profile);
        }

        private static void ApplyFont(Text target, Font font, int fontSize, Color color)
        {
            if (target == null) return;
            if (font != null)
                target.font = font;
            target.fontSize = fontSize;
            target.color = color;
            RequestHighResChars(font);
        }

        private static void EnsureShadow(Text target, bool addShadow)
        {
            if (target == null) return;
            var existing = target.GetComponent<Shadow>();
            if (addShadow)
            {
                if (existing == null)
                {
                    existing = target.gameObject.AddComponent<Shadow>();
                    existing.effectColor = new Color(0f, 0f, 0f, 0.4f);
                    existing.effectDistance = new Vector2(1f, -1f);
                }
            }
            else
            {
                if (existing != null)
                    UnityEngine.Object.DestroyImmediate(existing);
            }
        }

        private static void RequestHighResChars(Font font)
        {
            if (font == null || !font.dynamic) return;
            font.RequestCharactersInTexture(
                "0123456789abcdefghijklmnopqrstuvwxyz" +
                "伤害暴击击退锋锐抽牌销毁获得还魂护甲" +
                "通脉草飞刀芭蕉扇神火开山斧锁子甲筋" +
                "斗云孙悟空阴阳土木水金火万☯·+%", 64);
        }

        public void SetHighlight(bool on)
        {
            if (on && (!_canAfford ||
                (cardButton != null && !cardButton.interactable)))
            {
                on = false;
            }

            CardPresentationProfile profile =
                presentationProfile != null
                    ? presentationProfile
                    : CardPresentationProfile.LoadDefault();
            CardPresentationProfile.ColorStyle style = profile?.GetStyle(_cardData.color);
            Color glowColor = style?.headerColor ?? Color.white;

            if (on)
            {
                float rimAlpha = profile?.glowRimAlpha ?? 0.8f;
                float proxyWidth = profile?.glowProxyWidth ?? 4f;
                float colorMult = profile?.glowColorMultiplier ?? 1.8f;
                float proxyAlpha = profile?.glowProxyAlpha ?? 0.5f;
                float scatterAlpha = profile?.glowScatterAlpha ?? 0.12f;

                // 边缘发光（rim glow — 描边 sprite，仅边缘）
                if (hoverOrSelectedOutline != null)
                {
                    hoverOrSelectedOutline.color = new Color(glowColor.r, glowColor.g, glowColor.b, rimAlpha);
                    hoverOrSelectedOutline.gameObject.SetActive(true);
                }

                // 边缘外发光（rim glow proxy — 略大于卡牌，仅边缘，fillCenter=false）
                if (glowProxyImage != null)
                {
                    int radius = Mathf.RoundToInt(profile?.cornerRadius ?? 0f);
                    glowProxyImage.sprite = RoundedRectSpriteGenerator.GetOutline(radius, Mathf.RoundToInt(proxyWidth));
                    glowProxyImage.type = Image.Type.Sliced;
                    glowProxyImage.fillCenter = false;
                    glowProxyImage.color = new Color(
                        Mathf.Min(glowColor.r * colorMult, 1f),
                        Mathf.Min(glowColor.g * colorMult, 1f),
                        Mathf.Min(glowColor.b * colorMult, 1f),
                        proxyAlpha);
                    glowProxyImage.gameObject.SetActive(true);
                }

                // 外散光（比卡牌大，低 alpha，类型色扩散）
                // 暂时注释，以后调试
                // if (glowScatterImage != null)
                // {
                //     glowScatterImage.color = new Color(
                //         glowColor.r, glowColor.g, glowColor.b, scatterAlpha);
                //     glowScatterImage.gameObject.SetActive(true);
                // }
            }
            else
            {
                if (hoverOrSelectedOutline != null)
                    hoverOrSelectedOutline.gameObject.SetActive(false);
                if (glowProxyImage != null)
                {
                    glowProxyImage.color = Color.clear;
                    glowProxyImage.gameObject.SetActive(false);
                }
                // 暂时注释
                // if (glowScatterImage != null)
                // {
                //     glowScatterImage.color = Color.clear;
                //     glowScatterImage.gameObject.SetActive(false);
                // }
            }
        }

        public static GameObject CreateCardUI(Transform parent, ArtConfig artConfig)
        {
            return CardViewFactory.Create(parent).gameObject;
        }
    }
}
