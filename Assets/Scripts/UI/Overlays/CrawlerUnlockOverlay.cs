using System;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Overlays
{
    /// <summary>
    /// 英灵获得覆盖层。只显示已提交的奖励回执，不决定是否解锁。
    /// </summary>
    public sealed class CrawlerUnlockOverlay : MonoBehaviour
    {
        private GameObject _overlay;
        private Canvas _hostCanvas;

        /// <summary>设置专用宿主 Canvas。</summary>
        public void SetHostCanvas(Canvas canvas)
        {
            _hostCanvas = canvas;
        }

        /// <summary>显示英灵获得演出。</summary>
        public void Show(string displayName, Sprite artwork, Action onClosed)
        {
            Canvas canvas = _hostCanvas != null ? _hostCanvas : FindObjectOfType<Canvas>();
            if (canvas == null) { onClosed?.Invoke(); return; }

            _overlay = new GameObject("CrawlerUnlockOverlay",
                typeof(RectTransform), typeof(Image));
            _overlay.transform.SetParent(canvas.transform, false);
            var rt = _overlay.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = _overlay.GetComponent<Image>();
            bg.color = new Color(0.05f, 0.04f, 0.03f, 0.85f);
            bg.raycastTarget = true;

            // Portrait
            if (artwork != null)
            {
                var portraitGo = new GameObject("Portrait",
                    typeof(RectTransform), typeof(Image));
                portraitGo.transform.SetParent(_overlay.transform, false);
                var pImg = portraitGo.GetComponent<Image>();
                pImg.sprite = artwork;
                pImg.preserveAspect = true;
                var pRt = portraitGo.GetComponent<RectTransform>();
                pRt.anchorMin = new Vector2(0.5f, 0.5f);
                pRt.anchorMax = new Vector2(0.5f, 0.5f);
                pRt.sizeDelta = new Vector2(400f, 600f);
                pRt.anchoredPosition = new Vector2(0f, 50f);
            }

            // Title
            var titleGo = new GameObject("Title",
                typeof(RectTransform), typeof(Text));
            titleGo.transform.SetParent(_overlay.transform, false);
            var titleText = titleGo.GetComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 36;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = new Color(1f, 0.86f, 0.48f);
            titleText.text = $"已获得：{displayName}";
            var tRt = titleGo.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0.5f, 0f);
            tRt.anchorMax = new Vector2(0.5f, 0f);
            tRt.sizeDelta = new Vector2(600f, 80f);
            tRt.anchoredPosition = new Vector2(0f, 80f);

            // Confirm button
            var btnGo = new GameObject("ConfirmButton",
                typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(_overlay.transform, false);
            var btnBg = btnGo.GetComponent<Image>();
            btnBg.color = new Color(0.15f, 0.12f, 0.08f, 0.95f);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.5f, 0f);
            btnRt.anchorMax = new Vector2(0.5f, 0f);
            btnRt.sizeDelta = new Vector2(200f, 60f);
            btnRt.anchoredPosition = new Vector2(0f, 20f);

            var btnTextGo = new GameObject("Text",
                typeof(RectTransform), typeof(Text));
            btnTextGo.transform.SetParent(btnGo.transform, false);
            var btnText = btnTextGo.GetComponent<Text>();
            btnText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            btnText.fontSize = 24;
            btnText.alignment = TextAnchor.MiddleCenter;
            btnText.color = Color.white;
            btnText.text = "确认";
            var btRt = btnTextGo.GetComponent<RectTransform>();
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.offsetMin = Vector2.zero;
            btRt.offsetMax = Vector2.zero;

            var btn = btnGo.GetComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                Close();
                onClosed?.Invoke();
            });
        }

        /// <summary>关闭覆盖层。</summary>
        public void Close()
        {
            if (_overlay != null)
            {
                Destroy(_overlay);
                _overlay = null;
            }
        }

        private void OnDestroy()
        {
            Close();
        }
    }
}
