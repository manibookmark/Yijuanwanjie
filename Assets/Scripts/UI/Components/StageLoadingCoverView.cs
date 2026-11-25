using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Components
{
    /// <summary>One full-screen chapter cover; loading and residency stay outside UI.</summary>
    public sealed class StageLoadingCoverView : MonoBehaviour
    {
        // 试玩复审：暂时停用关卡入口 Loading 封面，保留原实现便于后续调整。
        // 只关闭表现；资源准备、输入租约和就绪握手仍由原 owner 管理。
        private static readonly bool EntranceCoverEnabled = false;
        private GameObject _root;
        private Image _background;
        private Text _title;
        private Text _status;

        public void Show(string title)
        {
            if (!EntranceCoverEnabled) { Hide(); return; }
            EnsureBuilt();
            _background.sprite = null;
            _background.color = Color.clear;
            _title.text = title;
            _status.text = "加载中…";
            _root.SetActive(true);
        }

        public void SetBackground(Sprite sprite)
        {
            if (!EntranceCoverEnabled) return;
            EnsureBuilt();
            _background.sprite = sprite;
            _background.color = sprite != null ? Color.white : Color.clear;
        }

        public void ShowFailure()
        {
            // 错误反馈仍须可见，不能随正常 Loading 提示一并隐藏。
            EnsureBuilt();
            _title.text = string.Empty;
            _status.text = "加载未完成，请返回后重试";
            _root.SetActive(true);
        }

        public void Hide() { if (_root != null) _root.SetActive(false); }

        private void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new GameObject("StageLoadingCover", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _root.transform.SetParent(transform, false);
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32750;
            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            Image backdrop = ImageChild("Backdrop", _root.transform,
                new Color(0.025f, 0.045f, 0.06f, 1f));
            backdrop.raycastTarget = true;
            _background = ImageChild("ChapterArtwork", _root.transform, Color.clear);
            _background.gameObject.AddComponent<AspectRatioFitter>().aspectMode =
                AspectRatioFitter.AspectMode.EnvelopeParent;
            _background.GetComponent<AspectRatioFitter>().aspectRatio = 2560f / 1080f;
            Image shade = ImageChild("PaperShade", _root.transform,
                new Color(0.015f, 0.035f, 0.055f, 0.38f));
            shade.raycastTarget = false;
            Font font = Resources.Load<Font>("Fonts/NotoSerifSC-Medium") ??
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _title = TextChild("ChapterTitle", font, 58, 0.22f);
            _status = TextChild("LoadingStatus", font, 28, 0.125f);
        }

        private Text TextChild(string name, Font font, int size, float height)
        {
            var child = new GameObject(name, typeof(Text));
            child.transform.SetParent(_root.transform, false);
            Text text = child.GetComponent<Text>();
            text.font = font; text.fontSize = size;
            text.color = new Color(0.94f, 0.94f, 0.85f, 1f);
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, height);
            rect.sizeDelta = new Vector2(1280f, 100f);
            return text;
        }

        private static Image ImageChild(string name, Transform parent, Color color)
        {
            var child = new GameObject(name, typeof(Image));
            child.transform.SetParent(parent, false);
            Image image = child.GetComponent<Image>(); image.color = color;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return image;
        }
    }
}
