using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 增益图标纵向容器。管理左侧（临时）或右侧（永久）一列 BuffIconView。
    /// </summary>
    public sealed class BuffBarView : MonoBehaviour
    {
        public enum Side { Left, Right }

        private BuffIconView[] _icons;
        private PowerUpGlyphConfig.GlyphInfo[] _glyphs;
        private Side _side;

        [Header("属性获得转场")]
        [Tooltip("属性图形在出牌区域成形所需时间。")]
        public float gainRevealDuration = 0.16f;
        [Tooltip("属性图形飞向对应 HUD 图标所需时间。")]
        public float gainFlightDuration = 0.52f;
        [Tooltip("飞行起点在画面中的归一化位置。")]
        public Vector2 gainSourceViewportPosition = new Vector2(0.5f, 0.2f);
        [Tooltip("大于 1 时先慢后快，形成向 HUD 吸入的物理加速度。")]
        [Range(1f, 3f)] public float gainAccelerationPower = 2.15f;

        private readonly Dictionary<StatType, int> _pendingValues =
            new Dictionary<StatType, int>();
        private readonly Dictionary<StatType, Coroutine> _gainRoutines =
            new Dictionary<StatType, Coroutine>();
        private readonly Dictionary<StatType, List<Image>> _gainImages =
            new Dictionary<StatType, List<Image>>();

        private readonly Dictionary<StatType, int> _gainVersions = new Dictionary<StatType, int>();

        public void Initialize(
            Side side,
            PowerUpGlyphConfig.GlyphInfo[] glyphs,
            BuffIconView[] icons)
        {
            _side = side;
            _glyphs = glyphs;
            _icons = icons;
        }

        public void Refresh(
            PlayerStats stats,
            Func<StatType, int> getCurrentStatValue = null)
        {
            if (_icons == null || _glyphs == null) return;

            for (int i = 0; i < _icons.Length; i++)
            {
                if (_icons[i] == null) continue;

                StatType? stat = _glyphs[i].StatType;
                int value = stat.HasValue && getCurrentStatValue != null
                    ? getCurrentStatValue(stat.Value)
                    : PowerUpGlyphConfig.GetStatValue(stats, _glyphs[i]);
                if (stat.HasValue && _pendingValues.ContainsKey(stat.Value))
                {
                    // Combat state is already authoritative, but the HUD holds
                    // its previous value until the transfer reaches this icon.
                    _pendingValues[stat.Value] = value;
                    continue;
                }
                _icons[i].Refresh(value);
            }
        }

        /// <summary>
        /// Presents any configured stat gain with the matching HUD glyph.
        /// Cards do not need visual-specific code: StatType selects both the
        /// destination and its color language. Returns false when this bar does
        /// not own the requested stat.
        /// </summary>
        public bool PlayStatGain(CombatStatChange change, RectTransform source = null)
        {
            int index = FindIconIndex(change.StatType);
            if (index < 0) return false;
            if (_pendingValues.ContainsKey(change.StatType) && !_gainRoutines.ContainsKey(change.StatType))
            {
                _pendingValues[change.StatType] = change.ResultingValue;
                return true;
            }
            if (change.Amount <= 0)
            { CancelGain(change.StatType, true); _icons[index]?.Refresh(change.ResultingValue); return true; }
            PrepareGain(change, index);
            BeginGain(change.StatType, index, source, null);
            return true;
        }

        /// <summary>Hold the old number during manifestation. A newer receipt
        /// supersedes this callback, so repeated casts cannot rewind the HUD.</summary>
        public Action<Vector2> PrepareStatGain(CombatStatChange change)
        {
            int index = FindIconIndex(change.StatType);
            if (index < 0) return null;
            int version = PrepareGain(change, index);
            bool consumed = false;
            return screen =>
            {
                if (consumed) return;
                consumed = true;
                if (this == null || !_gainVersions.TryGetValue(change.StatType, out int current) ||
                    current != version || !_pendingValues.ContainsKey(change.StatType)) return;
                BeginGain(change.StatType, index, null, screen);
            };
        }

        private int PrepareGain(CombatStatChange change, int index)
        {
            CancelGain(change.StatType, applyPendingValue: false);
            int version = _gainVersions.TryGetValue(change.StatType, out int old) ? old + 1 : 1;
            _gainVersions[change.StatType] = version;
            _pendingValues[change.StatType] = change.ResultingValue;
            _icons[index]?.SetGainPending(true);
            return version;
        }

        private void BeginGain(StatType stat, int index, RectTransform source, Vector2? sourceScreen)
        {
            BuffIconView icon = _icons[index];
            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            if (!isActiveAndEnabled || icon?.IconImage?.sprite == null || canvasRect == null)
            {
                CancelGain(stat, applyPendingValue: true);
                return;
            }
            _gainRoutines[stat] = StartCoroutine(GainRoutine(stat, index, canvas, canvasRect, source, sourceScreen));
        }

        /// <summary>只读返回属性图标中心，不向其他模块暴露可变图形组件。</summary>
        public bool TryGetStatIconScreenPosition(StatType stat, out Vector2 position)
        {
            position = default;
            int index = FindIconIndex(stat);
            if (index < 0 || _icons[index]?.IconImage == null ||
                !_icons[index].gameObject.activeInHierarchy) return false;
            RectTransform rect = _icons[index].IconImage.rectTransform;
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera;
            position = RectTransformUtility.WorldToScreenPoint(camera,
                rect.TransformPoint(rect.rect.center));
            return true;
        }

        private int FindIconIndex(StatType stat)
        {
            if (_icons == null || _glyphs == null)
                return -1;

            int count = Mathf.Min(_icons.Length, _glyphs.Length);
            for (int i = 0; i < count; i++)
            {
                if (_icons[i] != null && _glyphs[i].StatType == stat)
                    return i;
            }
            return -1;
        }

        private IEnumerator GainRoutine(
            StatType stat,
            int iconIndex,
            Canvas canvas,
            RectTransform canvasRect,
            RectTransform source, Vector2? sourceScreenPosition)
        {
            Canvas.ForceUpdateCanvases();
            BuffIconView icon = _icons[iconIndex];
            Sprite sprite = icon.IconImage.sprite;
            PowerUpGlyphConfig.GainPresentationStyle style =
                PowerUpGlyphConfig.GetGainPresentationStyle(stat);

            Vector2 iconSize = icon.IconImage.rectTransform.rect.size;
            if (iconSize.x <= 1f || iconSize.y <= 1f)
                iconSize = new Vector2(72f, 72f);
            float largestSide = Mathf.Max(iconSize.x, iconSize.y);
            float readableScale = PowerUpGlyphConfig.GainGlyphSize /
                Mathf.Max(1f, largestSide);
            Vector2 targetSize = iconSize * readableScale;
            float destinationScale = 1f / readableScale;

            var images = new List<Image>();
            const int trailCount = 3;
            for (int i = trailCount; i >= 1; i--)
            {
                Color trailColor = style.TrailColor;
                trailColor.a *= 1f - i * 0.2f;
                images.Add(CreateTransferImage(
                    canvas.transform,
                    $"StatGainTrail_{stat}_{i}",
                    sprite,
                    trailColor,
                    targetSize));
            }

            Image core = CreateTransferImage(
                canvas.transform,
                $"StatGainTransfer_{stat}",
                sprite,
                style.CoreColor,
                targetSize);
            images.Add(core);
            _gainImages[stat] = images;

            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
            Vector2 targetScreen = RectTransformUtility.WorldToScreenPoint(
                uiCamera,
                icon.IconImage.rectTransform.position);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    targetScreen,
                    uiCamera,
                    out Vector2 destination))
            {
                destination = Vector2.zero;
            }

            Rect bounds = canvasRect.rect;
            Vector2 start = new Vector2(
                Mathf.Lerp(
                    bounds.xMin,
                    bounds.xMax,
                    Mathf.Clamp01(gainSourceViewportPosition.x)),
                Mathf.Lerp(
                    bounds.yMin,
                    bounds.yMax,
                    Mathf.Clamp01(gainSourceViewportPosition.y)));
            if (source != null && source.gameObject.activeInHierarchy)
            {
                Canvas sourceCanvas = source.GetComponentInParent<Canvas>();
                Camera sourceCamera = sourceCanvas == null ||
                    sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                        ? null
                        : sourceCanvas.worldCamera;
                Vector2 sourceScreen = RectTransformUtility.WorldToScreenPoint(
                    sourceCamera,
                    source.position);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect,
                        sourceScreen,
                        uiCamera,
                        out Vector2 sourcePoint))
                {
                    start = sourcePoint;
                }
            }
            if (sourceScreenPosition.HasValue &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,
                    sourceScreenPosition.Value, uiCamera, out Vector2 manifestedPoint))
                start = manifestedPoint;
            RectTransform coreRect = core.rectTransform;
            coreRect.localPosition = new Vector3(start.x, start.y, 0f);

            for (int i = 0; i < trailCount; i++)
            {
                images[i].rectTransform.localPosition = coreRect.localPosition;
                SetImageAlpha(images[i], 0f);
            }

            float revealDuration = gainRevealDuration > 0.01f
                ? gainRevealDuration
                : 0.16f;
            float elapsed = 0f;
            while (elapsed < revealDuration)
            {
                float t = Mathf.Clamp01(elapsed / revealDuration);
                float eased = Smooth(t);
                // The shape begins already readable and translucent, then
                // gathers substance before it is pulled toward the HUD.
                float scale = Mathf.Lerp(0.72f, 1f, eased);
                coreRect.localScale = new Vector3(scale, scale, 1f);
                SetImageAlpha(
                    core,
                    style.CoreColor.a * Mathf.Lerp(0.3f, 0.74f, eased));
                coreRect.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Lerp(-9f, -5f, eased));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Vector2 control = (start + destination) * 0.5f +
                Vector2.up * Mathf.Clamp(
                    Mathf.Abs(destination.x - start.x) * 0.12f + 72f,
                    72f,
                    170f);
            float flightDuration = gainFlightDuration > 0.01f
                ? gainFlightDuration
                : 0.52f;
            elapsed = 0f;
            while (elapsed < flightDuration)
            {
                float t = Mathf.Clamp01(elapsed / flightDuration);
                // Power easing models acceleration: the glyph hangs briefly
                // near the card, then is pulled increasingly quickly into the
                // destination icon. The arrival pulse absorbs the impact.
                float eased = Mathf.Pow(
                    t,
                    gainAccelerationPower >= 1f
                        ? Mathf.Clamp(gainAccelerationPower, 1f, 3f)
                        : 2.15f);
                Vector2 position = EvaluateCurve(
                    start,
                    control,
                    destination,
                    eased);
                coreRect.localPosition = new Vector3(position.x, position.y, 0f);
                float scale = Mathf.Lerp(1f, destinationScale, eased);
                coreRect.localScale = new Vector3(scale, scale, 1f);
                SetImageAlpha(
                    core,
                    style.CoreColor.a * Mathf.Lerp(
                        0.74f,
                        1f,
                        Smooth(t)));
                coreRect.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Lerp(-5f, 0f, eased) +
                    Mathf.Sin(t * Mathf.PI * 2f) * 3f * (1f - t));

                Vector2 tangent =
                    2f * (1f - eased) * (control - start) +
                    2f * eased * (destination - control);
                Vector2 trailDirection = tangent.sqrMagnitude > 0.001f
                    ? tangent.normalized
                    : Vector2.up;
                for (int i = 0; i < trailCount; i++)
                {
                    // Keep the afterimages close to the moving core. They stay
                    // hidden during the slow launch so a full green duplicate
                    // cannot be mistaken for a second stat icon.
                    Vector2 trailPosition = position -
                        trailDirection * ((i + 1) * 10f);
                    RectTransform trailRect = images[i].rectTransform;
                    trailRect.localPosition = new Vector3(
                        trailPosition.x,
                        trailPosition.y,
                        0f);
                    float trailScale = scale * (0.72f - i * 0.06f);
                    trailRect.localScale = new Vector3(
                        trailScale,
                        trailScale,
                        1f);
                    float fadeIn = Smooth(Mathf.Clamp01(
                        (t - 0.34f - i * 0.025f) / 0.18f));
                    float fadeOut = 1f - Mathf.Clamp01((t - 0.86f) / 0.14f);
                    SetImageAlpha(
                        images[i],
                        style.TrailColor.a *
                        (0.22f - i * 0.05f) *
                        fadeIn * fadeOut);
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            CompleteGain(stat, iconIndex);
        }

        private static Image CreateTransferImage(
            Transform parent,
            string objectName,
            Sprite sprite,
            Color color,
            Vector2 size)
        {
            var transferObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            transferObject.transform.SetParent(parent, false);
            transferObject.transform.SetAsLastSibling();
            Image image = transferObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.sprite = sprite;
            image.color = color;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return image;
        }

        private static Vector2 EvaluateCurve(
            Vector2 start,
            Vector2 control,
            Vector2 destination,
            float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * start +
                2f * inverse * t * control +
                t * t * destination;
        }

        private static float Smooth(float value)
            => value * value * (3f - 2f * value);

        private static void SetImageAlpha(Image image, float alpha)
        {
            if (image == null) return;
            Color color = image.color;
            color.a = Mathf.Clamp01(alpha);
            image.color = color;
        }

        private void CompleteGain(StatType stat, int iconIndex)
        {
            DestroyGainImages(stat);
            _gainRoutines.Remove(stat);

            int resultingValue = _pendingValues.TryGetValue(
                stat,
                out int pendingValue)
                ? pendingValue
                : 0;
            _pendingValues.Remove(stat);

            BuffIconView icon = _icons != null &&
                iconIndex >= 0 && iconIndex < _icons.Length
                ? _icons[iconIndex]
                : null;
            if (icon == null)
                return;
            icon.SetGainPending(false);
            icon.Refresh(resultingValue);
            icon.PlayGainArrival();
        }

        private void CancelGain(StatType stat, bool applyPendingValue)
        {
            if (_gainRoutines.TryGetValue(stat, out Coroutine routine) &&
                routine != null)
            {
                StopCoroutine(routine);
            }
            _gainRoutines.Remove(stat);
            DestroyGainImages(stat);

            if (applyPendingValue &&
                _pendingValues.TryGetValue(stat, out int pendingValue))
            {
                int iconIndex = FindIconIndex(stat);
                if (iconIndex >= 0)
                    _icons[iconIndex].Refresh(pendingValue);
            }
            _pendingValues.Remove(stat);
            int index = FindIconIndex(stat);
            if (index >= 0) _icons[index]?.SetGainPending(false);
        }

        private void DestroyGainImages(StatType stat)
        {
            if (!_gainImages.TryGetValue(stat, out List<Image> images))
                return;

            for (int i = 0; i < images.Count; i++)
            {
                if (images[i] != null)
                    Destroy(images[i].gameObject);
            }
            _gainImages.Remove(stat);
        }

        /// <summary>预览某个属性的投影值（hover 时调用）。</summary>
        public void ShowPreview(StatType stat, int projectedValue)
        {
            if (_icons == null || _glyphs == null) return;

            for (int i = 0; i < _icons.Length; i++)
            {
                if (_icons[i] == null) continue;
                if (_glyphs[i].StatType == stat)
                {
                    _icons[i].ShowPreview(projectedValue);
                    break;
                }
            }
        }

        /// <summary>清除所有预览。</summary>
        public void ClearPreview()
        {
            if (_icons == null) return;

            for (int i = 0; i < _icons.Length; i++)
            {
                if (_icons[i] != null)
                    _icons[i].ClearPreview();
            }
        }

        /// <summary>Discards old transfers and invalidates their late receipts.</summary>
        public void ResetEncounterPresentation()
        {
            var pending = new List<StatType>(_pendingValues.Keys);
            for (int i = 0; i < pending.Count; i++)
                CancelGain(pending[i], applyPendingValue: false);
            var versions = new List<StatType>(_gainVersions.Keys);
            for (int i = 0; i < versions.Count; i++)
                unchecked { _gainVersions[versions[i]]++; }
            ClearPreview();
            if (_icons == null)
                return;
            for (int i = 0; i < _icons.Length; i++)
                _icons[i]?.Refresh(0);
        }

        private void OnDisable()
        {
            var stats = new List<StatType>(_pendingValues.Keys);
            for (int i = 0; i < stats.Count; i++)
                CancelGain(stats[i], applyPendingValue: true);
            _pendingValues.Clear();
        }
    }
}
