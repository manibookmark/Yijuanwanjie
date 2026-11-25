using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Meta;
using UnityEngine.EventSystems;
using WanjieScroll.UI;

namespace WanjieScroll.UI.Daozang
{
    /// <summary>
    /// 道藏界面中的单个符箓组件。
    /// 已修习=彩色符胆+朱砂印章+等级圆点，未修习=墨色符胆，锁定=灰阶。
    /// 选中态复用 hover 的暖纸色和轻微抬升。
    /// </summary>
    public sealed class DaozangTalismanView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        public Image BodyImage;
        public Image GlyphImage;
        public Image SealImage;
        public Text NameText;
        public RectTransform LevelDotsContainer;
        public GameObject LockRibbon;
        public Button Button;
        public Outline GlowOutline; // 旧版选中描边引用，现由 hover 态统一替代

        [Header("兼容旧版选中描边参数")]
        public Color glowColor = new(0.66f, 0.16f, 0.09f, 0.72f);
        public float glowDistanceX = 1.5f;
        public float glowDistanceY = -1.5f;

        private DaozangConfig.JingjueInfo _info;
        private bool _locked;
        private string _unlockHint;
        private bool _selected;
        private bool _hovered;
        private int _currentLevel;
        private DaozangTooltipTrigger _tooltipTrigger;
        private float _targetScale = 1f;
        private readonly List<Image> _cardDots = new();

        private static readonly Color ActiveColor       = Color.white;
        private static readonly Color InactiveColor     = new(0.20f, 0.17f, 0.12f, 0.65f);
        private static readonly Color LockedColor       = new(0.35f, 0.33f, 0.30f, 0.30f);
        private static readonly Color BodyLockedColor   = new(0.55f, 0.52f, 0.48f, 0.35f);
        private static readonly Color BodyHoverColor    = new(1f, 0.98f, 0.93f, 1f);
        private static readonly Color Cinnabar          = new(0.75f, 0.15f, 0.08f, 0.85f);
        private static readonly Color InkDeep           = new(0.06f, 0.05f, 0.04f, 1f);
        private static readonly Color TextDim           = new(0.45f, 0.42f, 0.36f, 0.7f);
        private static readonly Color DotFilledCard     = new(0.75f, 0.15f, 0.08f, 1f);
        private static readonly Color DotEmptyCard      = new(0.3f, 0.27f, 0.22f, 0.5f);

        public event Action<string> OnSelected;

        public void BindTooltip(CardTooltipView tooltip)
        {
            _tooltipTrigger = GetComponent<DaozangTooltipTrigger>() ??
                gameObject.AddComponent<DaozangTooltipTrigger>();
            _tooltipTrigger.Bind(tooltip, string.Empty);
            RefreshTooltip();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            if (_locked) return; // 锁定：不响应 hover
            _hovered = true;
            RefreshVisual();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            RefreshVisual();
        }

        public void Setup(DaozangConfig.JingjueInfo info, Sprite glyph, int level, bool locked)
        {
            _info = info;
            _currentLevel = level;
            _locked = locked;

            if (GlyphImage != null)
            {
                GlyphImage.sprite = glyph;
                GlyphImage.preserveAspect = true;
                GlyphImage.raycastTarget = false;
            }

            if (Button != null)
            {
                Button.onClick.RemoveAllListeners();
                Button.onClick.AddListener(() => OnSelected?.Invoke(_info.Id));
            }

            RefreshVisual();
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            RefreshVisual();
        }

        public void Refresh(int level, bool locked, string unlockHint = null)
        {
            _currentLevel = level;
            _locked = locked;
            _unlockHint = unlockHint;
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            bool purchased = _currentLevel > 0;

            if (GlyphImage != null)
            {
                GlyphImage.color = _locked
                    ? LockedColor
                    : purchased
                        ? ActiveColor
                        : InactiveColor;
            }

            if (NameText != null)
            {
                NameText.text = _info.Name;
                NameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                NameText.alignment = TextAnchor.MiddleCenter;
                NameText.fontSize = 16;
                NameText.color = _locked
                    ? TextDim
                    : InkDeep;
                NameText.raycastTarget = false;
            }

            if (LevelDotsContainer != null)
            {
                bool showDots = purchased && !_locked;
                LevelDotsContainer.gameObject.SetActive(showDots);

                if (showDots)
                    RebuildCardDots(_currentLevel, _info.MaxLevel);
            }

            if (Button != null)
                Button.interactable = !_locked; // 锁定：不可点击，未修习可点击可选中

            if (BodyImage != null)
            {
                BodyImage.color = _locked
                    ? BodyLockedColor
                    : (_selected || _hovered)
                        ? BodyHoverColor
                            : purchased
                                ? Color.white
                                : new Color(0.6f, 0.58f, 0.54f, 0.7f); // 未修习：半透明灰
            }

            // 只做轻微抬升，避免选中时遮挡相邻符箓或产生突兀跳变。
            _targetScale = _locked ? 1f : (_selected || _hovered) ? 1.012f : 1f;

            // 选中态完全复用 hover 的轻量表现，不额外叠加描边。
            if (GlowOutline != null)
            {
                GlowOutline.effectColor = new Color(
                    glowColor.r, glowColor.g, glowColor.b, 0f);
            }

            // 锁定项保留灰阶和 Tooltip，但不再显示封禁封条。
            // 组件字段仍保留以兼容旧 Prefab，避免旧实例重新把封条带回运行时。
            if (LockRibbon != null)
                LockRibbon.SetActive(false);

            RefreshTooltip();
        }

        private void Update()
        {
            float current = transform.localScale.x;
            float next = Mathf.Lerp(current, _targetScale,
                1f - Mathf.Exp(-16f * Time.unscaledDeltaTime));
            if (Mathf.Abs(next - _targetScale) < 0.0005f)
                next = _targetScale;
            transform.localScale = new Vector3(next, next, 1f);
        }

        private void OnDisable()
        {
            _hovered = false;
            _targetScale = 1f;
            transform.localScale = Vector3.one;
        }

        private void RefreshTooltip()
        {
            if (_tooltipTrigger == null || string.IsNullOrWhiteSpace(_info.Name))
                return;

            // 每张符箓都提供可读的说明；锁定状态只追加解锁提示，
            // 不再把未解锁文字固定渲染在列表卡片上。
            string text = _info.Name;
            if (!string.IsNullOrWhiteSpace(_info.EffectText))
                text += $"\n{_info.EffectText}";
            if (_locked)
                text += "\n" + (string.IsNullOrWhiteSpace(_unlockHint)
                    ? "此经诀尚未开放。" : _unlockHint);
            _tooltipTrigger.SetText(text);
        }

        private void RebuildCardDots(int currentLevel, int maxLevel)
        {
            foreach (var dot in _cardDots)
            {
                if (dot == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(dot.gameObject);
                else
                    DestroyImmediate(dot.gameObject);
            }
            _cardDots.Clear();

            if (LevelDotsContainer == null) return;

            float dotSize = 8f;
            float dotSpacing = 4f;
            float totalW = maxLevel * dotSize + (maxLevel - 1) * dotSpacing;
            float startX = -totalW / 2f + dotSize / 2f;

            for (int i = 0; i < maxLevel; i++)
            {
                var go = new GameObject($"CardDot{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(LevelDotsContainer, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new(0.5f, 0.5f);
                rt.anchorMax = new(0.5f, 0.5f);
                rt.pivot = new(0.5f, 0.5f);
                rt.sizeDelta = new(dotSize, dotSize);
                rt.anchoredPosition = new(startX + i * (dotSize + dotSpacing), 0f);
                Image img = go.GetComponent<Image>();
                img.color = i < currentLevel ? DotFilledCard : DotEmptyCard;
                img.raycastTarget = false;
                _cardDots.Add(img);
            }
        }
    }
}
