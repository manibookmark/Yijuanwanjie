using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using WanjieScroll.Core;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 卡牌表现的权威配置。
    /// 测试场景只调整本资产；正式场景与预制体读取同一份数据，避免复制锚点和字号。
    /// </summary>
    [CreateAssetMenu(
        fileName = "DefaultCardPresentation",
        menuName = "万界长卷/UI/卡牌表现配置")]
    public sealed class CardPresentationProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 6;
        public const string DefaultResourcePath = "UI/Card/DefaultCardPresentation";

        [Serializable]
        public struct NormalizedRect
        {
            public Vector2 anchorMin;
            public Vector2 anchorMax;

            public NormalizedRect(float minX, float minY, float maxX, float maxY)
            {
                anchorMin = new Vector2(minX, minY);
                anchorMax = new Vector2(maxX, maxY);
            }

            public bool IsValid =>
                anchorMin.x >= 0f &&
                anchorMin.y >= 0f &&
                anchorMax.x <= 1f &&
                anchorMax.y <= 1f &&
                anchorMin.x < anchorMax.x &&
                anchorMin.y < anchorMax.y;

            public void Apply(RectTransform target)
            {
                if (target == null) return;
                target.anchorMin = anchorMin;
                target.anchorMax = anchorMax;
                target.offsetMin = Vector2.zero;
                target.offsetMax = Vector2.zero;
            }
        }

        /// <summary>
        /// 每种 CardColor 的纯视觉样式。布局是全局共享的，不放在这里。
        /// </summary>
        [Serializable]
        public sealed class ColorStyle
        {
            public CardColor color;
            public Color headerColor = Color.white;
            public Color borderColor = Color.white;
            public Color headerTextColor = Color.white;
            public Color costFillColor = Color.clear;
            public Color costBorderColor = Color.white;
            [Range(0f, 1f)] public float inkOpacity = 0.24f;
        }

        /// <summary>旧版单卡印位配置，仅保留序列化兼容；正式布局已统一。</summary>
        public enum SealAnchor
        {
            BottomRight,
            BottomLeft
        }

        /// <summary>
        /// 卡面表现模式。
        /// </summary>
        public enum PresentationMode
        {
            ProgrammaticArtwork,
            LegacyFullCard
        }

        [Serializable]
        public sealed class ArtworkBinding
        {
            [Tooltip("稳定内容 ID；禁止使用卡牌显示名称作为关联键。")]
            public string contentId;
            public Sprite artwork;
            [Range(0.1f, 3f)] public float scale = 1f;
            public Vector2 normalizedOffset = Vector2.zero;
            [HideInInspector] public SealAnchor sealAnchor =
                SealAnchor.BottomRight;
            [HideInInspector] public Vector2 sealOffset = Vector2.zero;
            public PresentationMode presentationMode = PresentationMode.ProgrammaticArtwork;
        }

        // ── 身份与版本 ──
        [Header("身份与版本")]
        public int schemaVersion = CurrentSchemaVersion;
        public string profileId = "ui.card.standard.v1";
        [Min(1)] public int revision = 1;

        // ── 抽牌动画 ──
        [Header("抽牌动画")]
        [Min(0.01f)] public float drawCardDuration = 0.44f;
        [Min(0f)] public float drawCardStagger = 0.08f;

        // ── 基准尺寸 ──
        [Header("基准尺寸")]
        public Vector2 referenceSize = new Vector2(200f, 300f);
        public Vector2 sourceArtSize = new Vector2(1024f, 1024f);

        // ── 共享布局区域（所有类型共用） ──
        [Header("共享布局区域")]
        public NormalizedRect artworkRect = new NormalizedRect(0.04f, 0.19f, 0.96f, 0.86f);
        public NormalizedRect sealLayerRect = new NormalizedRect(0f, 0.19f, 1f, 0.84f);
        public NormalizedRect typeHeaderRect = new NormalizedRect(0.02f, 0.84f, 0.98f, 0.985f);
        public NormalizedRect costFrameRect = new NormalizedRect(0.05f, 0.855f, 0.22f, 0.965f);
        public NormalizedRect nameRect = new NormalizedRect(0.20f, 0.855f, 0.80f, 0.965f);
        public NormalizedRect descriptionRect = new NormalizedRect(0.08f, 0.035f, 0.92f, 0.18f);

        // ── 外观 ──
        [Header("外观")]
        [Range(0f, 20f)] public float cornerRadius = 0f;
        [Range(0f, 5f)] public float borderWidth = 1f;
        [Range(0f, 5f)] public float costBorderWidth = 1f;
        public Color cardBaseColor = HexColor("#F2E8D2");
        public Color bodyTextColor = HexColor("#2B2118");
        [Range(0f, 1f)] public float paperFiberAlpha = 0.12f;
        [Range(0f, 1f)] public float inkMasterAlpha = 1.0f;

        // ── 灵印布局 ──
        [Header("灵印布局")]
        public float sealSize = 44f;
        [FormerlySerializedAs("sealSpacing")]
        [Tooltip("印位左边距、上边距以及相邻印位间距共用此值。")]
        public float sealInsetAndGap = 8f;
        public int sealMaxCount = 3;

        // ── 共享素材 ──
        [Header("共享素材")]
        public Sprite paperFiberSprite;
        public Sprite inkWashSprite;

        // ── 字体 ──
        [Header("字体")]
        public Font titleFont;
        public Font bodyFont;
        public Font numberFont;
        [Min(1)] public int nameFontSize = 18;
        [Min(1)] public int costFontSize = 28;
        [Min(1)] public int descriptionFontSize = 18;
        public int nameMinFontSize = 14;
        public int costMinFontSize = 18;
        public int descriptionMinFontSize = 14;

        // ── 交互 ──
        [Header("交互")]
        [Min(1f)] public float hoverScale = 1.20f;
        [Min(0f)] public float hoverLiftY = 30f;
        [Min(0.01f)] public float hoverEnterDuration = 0.18f;
        [Min(0.01f)] public float hoverExitDuration = 0.22f;
        [Min(1)] public int hoverDescriptionFontSize = 24;
        [Min(1f)] public float hoverDescriptionHeightMultiplier = 2.5f;
        public Color hoverOutlineColor = HexColor("#D8B768CC");
        public Color selectedOutlineColor = HexColor("#D8B768D9");
        [Tooltip("灵力不足时费用文字颜色；不压暗卡面。")]
        public Color insufficientCostColor = HexColor("#8A8177");

        [Header("Hover 发光参数")]
        [Tooltip("边缘描边发光透明度")]
        [Range(0f, 1f)] public float glowRimAlpha = 0.8f;
        [Tooltip("边缘发光（GlowProxy）宽度（像素）")]
        [Range(1f, 10f)] public float glowProxyWidth = 4f;
        [Tooltip("边缘发光颜色倍率（>1 模拟 HDR 泛光）")]
        [Range(1f, 3f)] public float glowColorMultiplier = 1.8f;
        [Tooltip("边缘发光透明度")]
        [Range(0f, 1f)] public float glowProxyAlpha = 0.5f;
        [Tooltip("外散光扩展范围（像素）")]
        [Range(2f, 30f)] public float glowScatterSpread = 10f;
        [Tooltip("外散光透明度")]
        [Range(0f, 0.5f)] public float glowScatterAlpha = 0.12f;

        // ── 七种类型样式 ──
        [Header("按类型样式")]
        public List<ColorStyle> colorStyles = new List<ColorStyle>();

        // ── 卡面绑定 ──
        [Header("卡面绑定")]
        public List<ArtworkBinding> artworkBindings = new List<ArtworkBinding>();

        // ── 旧字段保留兼容（不再使用） ──
        [HideInInspector] public NormalizedRect artRect;
        [HideInInspector] public NormalizedRect colorTagRect;
        [HideInInspector] public NormalizedRect durabilityRect;
        [HideInInspector] public Color nameColor = new Color(0.12f, 0.08f, 0.04f);
        [HideInInspector] public Color costColor = Color.black;
        [HideInInspector] public Color descriptionColor = Color.black;
        [HideInInspector] public int colorTagFontSize = 16;
        [HideInInspector] public float inkWashAlpha = 0.24f;

        private static CardPresentationProfile _runtimeFallback;

        public static CardPresentationProfile LoadDefault()
        {
            CardPresentationProfile profile =
                Resources.Load<CardPresentationProfile>(DefaultResourcePath);
            return profile != null ? profile : RuntimeFallback;
        }

        public ColorStyle GetStyle(CardColor color)
        {
            EnsureColorStyles();
            ColorStyle exact =
                colorStyles.Find(item => item != null && item.color == color);
            return exact ?? colorStyles[0];
        }

        public ArtworkBinding GetBinding(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId) ||
                artworkBindings == null)
            {
                return null;
            }
            return artworkBindings.Find(
                item =>
                    item != null &&
                    string.Equals(
                        item.contentId,
                        contentId,
                        StringComparison.OrdinalIgnoreCase));
        }

        public Sprite GetArtwork(string contentId)
        {
            ArtworkBinding binding = GetBinding(contentId);
            return binding?.artwork;
        }

        public void SetArtwork(string contentId, Sprite artwork)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException(
                    "卡面关联必须提供稳定 contentId。",
                    nameof(contentId));

            artworkBindings ??= new List<ArtworkBinding>();
            ArtworkBinding binding = GetBinding(contentId);
            if (binding == null)
            {
                binding = new ArtworkBinding { contentId = contentId };
                artworkBindings.Add(binding);
            }
            binding.artwork = artwork;
        }

        public void IncrementRevision()
        {
            revision = Mathf.Max(1, revision + 1);
        }

        // ── 旧 API 兼容：把 ColorStyle 列表当作旧 ColorLayout 返回 ──
        // 外部调用者仍可使用 GetLayout，但布局字段已移到全局。
        [Obsolete("Use GetStyle instead. Layout is now global.")]
        public ColorStyle GetLayout(CardColor color) => GetStyle(color);

        public void ResetToDefaults()
        {
            schemaVersion = CurrentSchemaVersion;
            profileId = "ui.card.standard.v1";
            revision = Mathf.Max(1, revision);
            referenceSize = new Vector2(200f, 300f);
            sourceArtSize = new Vector2(1024f, 1024f);

            artworkRect = new NormalizedRect(0.04f, 0.19f, 0.96f, 0.86f);
            sealLayerRect = new NormalizedRect(0f, 0.19f, 1f, 0.84f);
            typeHeaderRect = new NormalizedRect(0.02f, 0.84f, 0.98f, 0.985f);
            costFrameRect = new NormalizedRect(0.05f, 0.855f, 0.22f, 0.965f);
            nameRect = new NormalizedRect(0.20f, 0.855f, 0.80f, 0.965f);
            descriptionRect = new NormalizedRect(0.08f, 0.035f, 0.92f, 0.18f);

            cornerRadius = 0f;
            borderWidth = 1f;
            costBorderWidth = 1f;
            cardBaseColor = HexColor("#F2E8D2");
            bodyTextColor = HexColor("#2B2118");
            paperFiberAlpha = 0.12f;
            inkMasterAlpha = 1.0f;

            sealSize = 44f;
            sealInsetAndGap = 8f;
            sealMaxCount = 3;

            nameFontSize = 18;
            costFontSize = 28;
            descriptionFontSize = 18;
            nameMinFontSize = 14;
            costMinFontSize = 18;
            descriptionMinFontSize = 14;

            hoverScale = 1.20f;
            hoverLiftY = 30f;
            hoverEnterDuration = 0.18f;
            hoverExitDuration = 0.22f;
            hoverDescriptionFontSize = 24;
            hoverDescriptionHeightMultiplier = 2.5f;
            hoverOutlineColor = HexColor("#D8B768CC");
            selectedOutlineColor = HexColor("#D8B768D9");
            insufficientCostColor = HexColor("#8A8177");

            glowRimAlpha = 0.8f;
            glowProxyWidth = 4f;
            glowColorMultiplier = 1.8f;
            glowProxyAlpha = 0.5f;
            glowScatterSpread = 10f;
            glowScatterAlpha = 0.12f;

            colorStyles = new List<ColorStyle>();
            foreach (CardColor color in Enum.GetValues(typeof(CardColor)))
                colorStyles.Add(CreateColorStyle(color));
            artworkBindings ??= new List<ArtworkBinding>();
        }

        public List<string> ValidateProfile()
        {
            var errors = new List<string>();
            if (schemaVersion != CurrentSchemaVersion)
                errors.Add($"schemaVersion 必须为 {CurrentSchemaVersion}。");
            if (string.IsNullOrWhiteSpace(profileId))
                errors.Add("profileId 不能为空。");
            if (referenceSize.x <= 0f || referenceSize.y <= 0f)
                errors.Add("referenceSize 必须大于 0。");
            if (sourceArtSize.x != 1024f || sourceArtSize.y != 1024f)
                errors.Add("sourceArtSize 必须为 1024×1024。");
            if (drawCardDuration <= 0f)
                errors.Add("drawCardDuration 必须大于 0。");
            if (drawCardStagger < 0f)
                errors.Add("drawCardStagger 不能为负。");
            if (insufficientCostColor.a <= 0f)
                errors.Add("insufficientCostColor 不能全透明。");

            if (!artworkRect.IsValid) errors.Add("artworkRect 不合法。");
            if (!sealLayerRect.IsValid) errors.Add("sealLayerRect 不合法。");
            if (!typeHeaderRect.IsValid) errors.Add("typeHeaderRect 不合法。");
            if (!costFrameRect.IsValid) errors.Add("costFrameRect 不合法。");
            if (!nameRect.IsValid) errors.Add("nameRect 不合法。");
            if (!descriptionRect.IsValid) errors.Add("descriptionRect 不合法。");

            EnsureColorStyles();
            var colors = new HashSet<CardColor>();
            foreach (ColorStyle style in colorStyles)
            {
                if (style == null)
                {
                    errors.Add("colorStyles 不能包含 null。");
                    continue;
                }
                if (!colors.Add(style.color))
                    errors.Add($"类型样式重复：{style.color}。");
                if (style.costFillColor.a != 0f)
                    errors.Add($"{style.color} 的 CostFill 必须全透明。");
                if (style.costBorderColor.a == 0f)
                    errors.Add($"{style.color} 的 CostBorder 不能透明。");
            }
            foreach (CardColor color in Enum.GetValues(typeof(CardColor)))
            {
                if (!colors.Contains(color))
                    errors.Add($"缺少类型样式：{color}。");
            }

            var artworkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (artworkBindings != null)
            {
                foreach (ArtworkBinding binding in artworkBindings)
                {
                    if (binding == null)
                    {
                        errors.Add("artworkBindings 不能包含 null。");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(binding.contentId))
                    {
                        errors.Add("卡面关联的 contentId 不能为空。");
                    }
                    else if (!artworkIds.Add(binding.contentId))
                    {
                        errors.Add($"卡面关联 contentId 重复：{binding.contentId}。");
                    }
                    if (binding.presentationMode == PresentationMode.ProgrammaticArtwork
                        && binding.artwork == null)
                    {
                        errors.Add($"ProgrammaticArtwork 模式缺少图片：{binding.contentId}。");
                    }
                }
            }

            return errors;
        }

        private static CardPresentationProfile RuntimeFallback
        {
            get
            {
                if (_runtimeFallback != null) return _runtimeFallback;
                _runtimeFallback = CreateInstance<CardPresentationProfile>();
                _runtimeFallback.name = "RuntimeCardPresentationFallback";
                _runtimeFallback.hideFlags = HideFlags.HideAndDontSave;
                _runtimeFallback.ResetToDefaults();
                return _runtimeFallback;
            }
        }

        private void EnsureColorStyles()
        {
            colorStyles ??= new List<ColorStyle>();
            foreach (CardColor color in Enum.GetValues(typeof(CardColor)))
            {
                if (colorStyles.Find(item => item != null && item.color == color) == null)
                    colorStyles.Add(CreateColorStyle(color));
            }
        }

        // ── 权威配色表（文档第七节） ──
        private static readonly Color HeaderWarmText = HexColor("#F2EBD9");
        private static readonly Color HeaderDeepText = HexColor("#1F140A");
        private static readonly Color InkWashRGB = HexColor("#1B1714");
        private static readonly Color CostBorderWarm = HexColorA("#F2EBD9B8");
        private static readonly Color CostBorderDeep = HexColorA("#1F140AB8");

        private static ColorStyle CreateColorStyle(CardColor color)
        {
            Color header, border, text;
            float inkAlpha;

            switch (color)
            {
                case CardColor.Huo:
                    header = HexColor("#A84C3D"); border = HexColor("#672C25");
                    text = HeaderWarmText; inkAlpha = 0.24f; break;
                case CardColor.Tu:
                    header = HexColor("#8C6745"); border = HexColor("#533C29");
                    text = HeaderWarmText; inkAlpha = 0.22f; break;
                case CardColor.Mu:
                    header = HexColor("#52745B"); border = HexColor("#304936");
                    text = HeaderWarmText; inkAlpha = 0.22f; break;
                case CardColor.Shui:
                    header = HexColor("#3D6071"); border = HexColor("#243B48");
                    text = HeaderWarmText; inkAlpha = 0.26f; break;
                case CardColor.Jin:
                    header = HexColor("#C8B98C"); border = HexColor("#80704C");
                    text = HeaderDeepText; inkAlpha = 0.18f; break;
                case CardColor.Yang:
                    header = HexColor("#E7DDC2"); border = HexColor("#A28A58");
                    text = HeaderDeepText; inkAlpha = 0.18f; break;
                case CardColor.Yin:
                    header = HexColor("#59465F"); border = HexColor("#302537");
                    text = HeaderWarmText; inkAlpha = 0.30f; break;
                default:
                    header = HexColor("#A84C3D"); border = HexColor("#672C25");
                    text = HeaderWarmText; inkAlpha = 0.24f; break;
            }

            bool isLightHeader = color == CardColor.Jin || color == CardColor.Yang;

            return new ColorStyle
            {
                color = color,
                headerColor = header,
                borderColor = border,
                headerTextColor = text,
                costFillColor = Color.clear,
                costBorderColor = isLightHeader ? CostBorderDeep : CostBorderWarm,
                inkOpacity = inkAlpha
            };
        }

        /// <summary>解析 #RRGGBB 为不透明 Color。</summary>
        public static Color HexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#') return Color.white;
            hex = hex.Substring(1);
            if (hex.Length != 6) return Color.white;
            float r = int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            float g = int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            float b = int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            return new Color(r, g, b, 1f);
        }

        /// <summary>解析 #RRGGBBAA 为带 Alpha 的 Color。</summary>
        public static Color HexColorA(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#') return Color.white;
            hex = hex.Substring(1);
            if (hex.Length != 8) return HexColor("#" + hex);
            float r = int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            float g = int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            float b = int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            float a = int.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            return new Color(r, g, b, a);
        }
    }
}
