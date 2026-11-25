using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Battle
{
    /// <summary>战斗内 BOSS 登场短台词；只负责文字淡入淡出。</summary>
    [DisallowMultipleComponent]
    public sealed class BossEntranceSpeechView : MonoBehaviour
    {
        private CanvasGroup _group;
        private RectTransform _panel;
        private Text _speaker;
        private Text _line;
        private Coroutine _routine;

        public static BossEntranceSpeechView Create(Transform canvasRoot)
        {
            if (canvasRoot == null)
                return null;
            var host = new GameObject(
                "BossEntranceSpeech",
                typeof(RectTransform),
                typeof(CanvasGroup));
            RectTransform root = host.GetComponent<RectTransform>();
            root.SetParent(canvasRoot, false);
            root.anchorMin = new Vector2(0.5f, 0.72f);
            root.anchorMax = root.anchorMin;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(780f, 92f);
            root.anchoredPosition = Vector2.zero;

            var view = host.AddComponent<BossEntranceSpeechView>();
            view._group = host.GetComponent<CanvasGroup>();
            view._group.alpha = 0f;
            view._group.interactable = false;
            view._group.blocksRaycasts = false;
            view.Build(root);
            host.SetActive(true);
            return view;
        }

        public void Show(string speaker, string line, float duration)
        {
            if (_group == null || string.IsNullOrWhiteSpace(line))
                return;
            if (_routine != null)
                StopCoroutine(_routine);
            _speaker.text = string.IsNullOrWhiteSpace(speaker)
                ? "BOSS"
                : speaker.Trim();
            _line.text = line.Trim();
            _routine = StartCoroutine(ShowRoutine(Mathf.Max(0.6f, duration)));
        }

        public void HideImmediate()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            if (_group != null)
                _group.alpha = 0f;
        }

        private void Build(RectTransform root)
        {
            var panelObject = new GameObject(
                "InkPanel",
                typeof(RectTransform),
                typeof(Image));
            _panel = panelObject.GetComponent<RectTransform>();
            _panel.SetParent(root, false);
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.one;
            _panel.offsetMin = Vector2.zero;
            _panel.offsetMax = Vector2.zero;
            Image background = panelObject.GetComponent<Image>();
            background.color = new Color(0.035f, 0.025f, 0.045f, 0.82f);
            background.raycastTarget = false;

            Font heavy = Resources.Load<Font>("Fonts/NotoSerifSC-Heavy") ??
                         Resources.GetBuiltinResource<Font>(
                             "LegacyRuntime.ttf");
            Font body = Resources.Load<Font>("Fonts/NotoSerifSC-Medium") ??
                        heavy;
            _speaker = CreateText(
                _panel,
                "Speaker",
                heavy,
                25,
                new Color(0.94f, 0.68f, 0.28f, 1f),
                TextAnchor.MiddleCenter,
                new Vector2(0f, 0f),
                new Vector2(0.22f, 1f));
            _line = CreateText(
                _panel,
                "Line",
                body,
                31,
                new Color(0.96f, 0.92f, 0.84f, 1f),
                TextAnchor.MiddleLeft,
                new Vector2(0.22f, 0f),
                new Vector2(1f, 1f));
        }

        private IEnumerator ShowRoutine(float duration)
        {
            const float fadeIn = 0.12f;
            const float fadeOut = 0.24f;
            float elapsed = 0f;
            Vector2 rest = Vector2.zero;
            while (elapsed < fadeIn)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeIn);
                _group.alpha = t;
                _panel.anchoredPosition = Vector2.Lerp(
                    new Vector2(0f, -10f),
                    rest,
                    t);
                yield return null;
            }
            _group.alpha = 1f;
            yield return new WaitForSecondsRealtime(
                Mathf.Max(0f, duration - fadeIn - fadeOut));
            elapsed = 0f;
            while (elapsed < fadeOut)
            {
                elapsed += Time.unscaledDeltaTime;
                _group.alpha = 1f - Mathf.Clamp01(elapsed / fadeOut);
                yield return null;
            }
            _group.alpha = 0f;
            _routine = null;
        }

        private static Text CreateText(
            RectTransform parent,
            string objectName,
            Font font,
            int size,
            Color color,
            TextAnchor alignment,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            var host = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Text));
            RectTransform rect = host.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(14f, 8f);
            rect.offsetMax = new Vector2(-14f, -8f);
            Text text = host.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Normal;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void OnDisable()
        {
            HideImmediate();
        }
    }
}
