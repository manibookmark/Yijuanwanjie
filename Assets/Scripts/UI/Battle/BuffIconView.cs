using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 单个增益图标：图标图片 + 数值文字。
    /// value > 0 时彩色显示，value == 0 时墨色半透明。
    /// hover 时显示描述 tooltip。
    /// </summary>
    public sealed class BuffIconView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image IconImage;
        public Text ValueText;

        private PowerUpGlyphConfig.GlyphInfo _info;
        private Color _activeColor = Color.white;
        private Color _inactiveColor = new Color(0.15f, 0.12f, 0.08f, 0.5f);
        private static readonly Color PreviewColor = new Color(0.4f, 0.9f, 0.5f, 1f);

        private int _currentRawValue;
        private int _previewRawValue;
        private bool _previewActive;
        private bool _gainPending;
        private Coroutine _gainPulse;

        private BuffTooltipView _tooltip;

        public void Setup(
            PowerUpGlyphConfig.GlyphInfo info,
            Sprite sprite,
            Color activeColor,
            Color inactiveColor,
            int fontSize)
        {
            _info = info;
            _activeColor = activeColor;
            _inactiveColor = inactiveColor;

            if (IconImage != null)
            {
                IconImage.sprite = sprite;
                IconImage.preserveAspect = true;
                IconImage.raycastTarget = true;
            }

            if (ValueText != null)
            {
                ValueText.font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
                ValueText.fontSize = fontSize;
                ValueText.alignment = TextAnchor.MiddleCenter;
                ValueText.raycastTarget = false;
                ValueText.horizontalOverflow = HorizontalWrapMode.Overflow;
                ValueText.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        public void Refresh(int rawValue)
        {
            _currentRawValue = rawValue;
            if (!_previewActive)
                ApplyDisplay(rawValue, _activeColor);
        }

        /// <summary>Reserve the receiving slot without publishing the pending value.</summary>
        public void SetGainPending(bool pending)
        {
            _gainPending = pending;
            ApplyDisplay(_previewActive ? _previewRawValue : _currentRawValue,
                _previewActive ? PreviewColor : _activeColor);
        }

        /// <summary>显示预览值（hover 时调用）。</summary>
        public void ShowPreview(int projectedValue)
        {
            _previewActive = true;
            _previewRawValue = projectedValue;
            ApplyDisplay(projectedValue, PreviewColor);
        }

        /// <summary>清除预览，恢复实际值。</summary>
        public void ClearPreview()
        {
            _previewActive = false;
            _previewRawValue = 0;
            ApplyDisplay(_currentRawValue, _activeColor);
        }

        /// <summary>
        /// Plays the common destination response after a text-free stat glyph
        /// reaches the HUD. The authoritative value must be refreshed first.
        /// </summary>
        public void PlayGainArrival()
        {
            if (!gameObject.activeInHierarchy)
                return;
            if (_gainPulse != null)
                StopCoroutine(_gainPulse);
            _gainPulse = StartCoroutine(GainArrivalRoutine());
        }

        private IEnumerator GainArrivalRoutine()
        {
            RectTransform rect = transform as RectTransform;
            if (rect == null)
            {
                _gainPulse = null;
                yield break;
            }

            const float duration = 0.24f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                float scale = t < 0.42f
                    ? Mathf.Lerp(0.72f, 1.18f, t / 0.42f)
                    : Mathf.Lerp(1.18f, 1f, (t - 0.42f) / 0.58f);
                rect.localScale = new Vector3(scale, scale, 1f);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            rect.localScale = Vector3.one;
            _gainPulse = null;
        }

        private void ApplyDisplay(int rawValue, Color valueColor)
        {
            bool active = _info.Id == "hand"
                ? rawValue > PowerUpGlyphConfig.BaseHandLimit
                : rawValue > 0;

            // 未购买/无增益时隐藏整个图标（含 hover 预览也只在有值时显示）
            if (!active && !_previewActive && !_gainPending)
            {
                gameObject.SetActive(false);
                return;
            }

            // 预览状态下即使当前值是 0 也显示（hover 增益卡预览效果）
            if (!active && _previewActive)
            {
                gameObject.SetActive(true);
                if (IconImage != null)
                    IconImage.color = PreviewColor;
                if (ValueText != null)
                {
                    ValueText.text = PowerUpGlyphConfig.FormatValue(_info, rawValue);
                    ValueText.color = PreviewColor;
                }
                return;
            }

            gameObject.SetActive(true);

            if (IconImage != null)
            {
                IconImage.color = _previewActive
                    ? PreviewColor
                    : valueColor;
            }

            if (ValueText != null)
            {
                if (active || _previewActive)
                {
                    ValueText.text = PowerUpGlyphConfig.FormatValue(
                        _info,
                        rawValue);
                    ValueText.color = _previewActive
                        ? PreviewColor
                        : valueColor;
                }
                else
                {
                    ValueText.text = string.Empty;
                }
            }
        }

        // ── Tooltip ──────────────────────────────────────────

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            ShowBuffTooltip();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideBuffTooltip();
        }

        private void ShowBuffTooltip()
        {
            if (string.IsNullOrEmpty(_info.Id)) return;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            BuffTooltipView next = BuffTooltipView.ForCanvas(canvas);
            if (_tooltip != next) HideBuffTooltip();
            _tooltip = next;
            int displayedValue = _previewActive ? _previewRawValue : _currentRawValue;
            string description = PowerUpGlyphConfig.FormatDescription(_info, displayedValue);
            string value = PowerUpGlyphConfig.FormatValue(_info, displayedValue);
            _tooltip.Show(this, $"<b>{_info.DisplayName}</b> {value}\n{description}");
        }

        private void HideBuffTooltip()
        {
            if (_tooltip != null) _tooltip.Hide(this);
        }

        private void OnDestroy() => HideBuffTooltip();

        private void OnDisable()
        {
            HideBuffTooltip();
            if (_gainPulse != null)
            {
                StopCoroutine(_gainPulse);
                _gainPulse = null;
            }
            transform.localScale = Vector3.one;
        }
    }
}
