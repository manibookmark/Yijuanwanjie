using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Components
{
    /// <summary>
    /// 场景切换通用加载指示器：画面底部中间一个低调的玉色转圈。
    /// 指示器挂在自建的高层 Canvas 上，随当前场景销毁，
    /// 适合异步转场等待期间使用。
    /// </summary>
    public static class SceneLoadingIndicator
    {
        private const string RootName = "SceneLoadingIndicator";
        private static readonly Color JadeHint = new Color(0.78f, 0.91f, 0.86f, 0.55f);

        private static GameObject _root;

        public static bool IsVisible => _root != null;

        public static void Show()
        {
            if (_root != null)
                return;

            _root = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var spinnerObject = new GameObject("Spinner", typeof(Image));
            spinnerObject.transform.SetParent(_root.transform, false);
            RectTransform rect = spinnerObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.05f);
            rect.anchorMax = new Vector2(0.5f, 0.05f);
            rect.sizeDelta = new Vector2(40f, 40f);
            rect.anchoredPosition = Vector2.zero;

            Image spinner = spinnerObject.GetComponent<Image>();
            spinner.sprite = CreateRingSprite();
            spinner.color = JadeHint;
            spinner.type = Image.Type.Filled;
            spinner.fillMethod = Image.FillMethod.Radial360;
            spinner.fillAmount = 0.28f;
            spinner.raycastTarget = false;
            spinnerObject.AddComponent<SceneLoadingIndicatorRotator>();
        }

        public static void Hide()
        {
            if (_root == null)
                return;
            Object.Destroy(_root);
            _root = null;
        }

        private static Sprite CreateRingSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            float cx = size / 2f;
            float cy = size / 2f;
            float outerR = size / 2f - 2f;
            float innerR = outerR - 6f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    int index = y * size + x;
                    pixels[index] = dist <= outerR && dist >= innerR
                        ? Color.white
                        : new Color(0, 0, 0, 0);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(
                texture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        private sealed class SceneLoadingIndicatorRotator : MonoBehaviour
        {
            private const float Speed = 140f;

            private void Update()
            {
                transform.Rotate(0f, 0f, -Speed * Time.deltaTime);
            }
        }
    }
}
