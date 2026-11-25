using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>Owns one resident's Duration glyph flight and cancellation.
    /// The resident presenter alone owns the count; this view only notifies
    /// arrival. A newer receipt invalidates old midpoint callbacks.</summary>
    [DisallowMultipleComponent]
    public sealed class CrawlerDurationGlyphTransfer : MonoBehaviour
    {
        private RectTransform _target;
        private Coroutine _routine;
        private readonly List<Image> _images = new List<Image>();
        private Action _onArrived;
        private int _version;
        private bool _pending;

        public void Bind(RectTransform target) => _target = target;

        public Action<Vector2> Prepare(Action onArrived)
        {
            Cancel();
            int version = ++_version;
            _pending = true;
            _onArrived = onArrived;
            bool consumed = false;
            return screen =>
            {
                if (consumed) return;
                consumed = true;
                if (this == null || version != _version || !_pending) return;
                if (!isActiveAndEnabled || _target == null)
                { Finish(); return; }
                _routine = StartCoroutine(Fly(screen));
            };
        }

        private IEnumerator Fly(Vector2 sourceScreen)
        {
            try
            {
                Canvas canvas = _target.GetComponentInParent<Canvas>();
                RectTransform canvasRect = canvas?.transform as RectTransform;
                if (canvasRect == null || !PowerUpGlyphConfig.TryGetGlyph(StatType.NingShen, out var glyph))
                    yield break;
                Sprite sprite = Resources.Load<Sprite>(glyph.SpritePath);
                if (sprite == null) yield break;
                Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sourceScreen,
                    camera, out Vector2 start)) yield break;
                var style = PowerUpGlyphConfig.GetGainPresentationStyle(StatType.NingShen);
                for (int i = 0; i < 3; i++)
                {
                    var go = new GameObject("ResidentDurationGlyph", typeof(RectTransform),
                        typeof(CanvasRenderer), typeof(Image));
                    go.transform.SetParent(canvas.transform, false);
                    Image image = go.GetComponent<Image>();
                    image.sprite = sprite;
                    image.raycastTarget = false;
                    image.color = i == 2 ? style.CoreColor : style.TrailColor;
                    RectTransform rect = image.rectTransform;
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = Vector2.one * (i == 2
                        ? PowerUpGlyphConfig.GainGlyphSize : PowerUpGlyphConfig.GainTrailSize);
                    rect.localPosition = start;
                    _images.Add(image);
                }
                const float duration = 0.52f;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    if (_target == null || !_target.gameObject.activeInHierarchy) yield break;
                    Vector2 targetScreen = RectTransformUtility.WorldToScreenPoint(camera,
                        _target.TransformPoint(_target.rect.center));
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, targetScreen,
                        camera, out Vector2 destination);
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    Vector2 control = (start + destination) * 0.5f + Vector2.up * 90f;
                    for (int i = 0; i < _images.Count; i++)
                    {
                        float u = Mathf.Pow(Mathf.Clamp01(t - (2 - i) * 0.045f), 2.15f);
                        Vector2 point = (1f - u) * (1f - u) * start +
                            2f * (1f - u) * u * control + u * u * destination;
                        _images[i].rectTransform.localPosition = point;
                        _images[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.25f, u);
                        Color color = _images[i].color;
                        color.a = (i == 2 ? 1f : 0.34f) * (1f - Mathf.Clamp01((t - 0.82f) / 0.18f));
                        _images[i].color = color;
                    }
                    yield return null;
                }
            }
            finally { Finish(); }
        }

        public void Cancel()
        {
            ++_version;
            if (_routine != null) StopCoroutine(_routine);
            Finish();
        }

        private void Finish()
        {
            _routine = null;
            foreach (Image image in _images)
                if (image != null) Destroy(image.gameObject);
            _images.Clear();
            bool pending = _pending;
            _pending = false;
            Action arrived = _onArrived;
            _onArrived = null;
            if (pending) arrived?.Invoke();
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();
    }
}
