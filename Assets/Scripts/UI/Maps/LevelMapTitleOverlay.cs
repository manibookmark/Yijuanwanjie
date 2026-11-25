using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Maps
{
    /// <summary>
    /// 唯一拥有地图标题的显现、缩移与常驻布局；调用方只提供标题及显隐意图。
    /// 顶部中央读完后归入屏幕左上 HUD，始终不拦截地图输入。
    /// </summary>
    public sealed class LevelMapTitleOverlay : MonoBehaviour
    {
        private const float RevealDuration = 0.48f;
        private const float ReadDuration = 3f;
        private const float DockDuration = 0.7f;
        private const float PresentationDuration =
            RevealDuration + ReadDuration + DockDuration;
        private const float DockScale = 0.58f;
        private const float StageWidth = 700f;

        private GameObject _root;
        private RectTransform _stage;
        private CanvasGroup _canvasGroup;
        private string _title;
        private float _elapsed;

        public GameObject Root => _root;
        public bool IsPresenting => _root != null &&
                                    _elapsed < PresentationDuration;

        public void Show(string title)
        {
            // Returning from a fight or replaying an entry sequence must not
            // restart a title that already belongs to this map.
            if (_root != null && string.Equals(_title, title))
                return;

            Close();
            if (string.IsNullOrWhiteSpace(title))
                return;

            _title = title;

            _root = new GameObject(
                "LevelMapTitleCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(CanvasGroup));
            _root.transform.SetParent(transform, false);

            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500;
            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _canvasGroup = _root.GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            var stageObject = new GameObject(
                "LevelMapTitleStage", typeof(RectTransform));
            stageObject.transform.SetParent(_root.transform, false);
            RectTransform stage = stageObject.GetComponent<RectTransform>();
            _stage = stage;
            stage.pivot = new Vector2(0.5f, 1f);
            stage.sizeDelta = new Vector2(StageWidth, 122f);

            var visualObject = new GameObject(
                "LevelMapTitleVisual",
                typeof(RectTransform),
                typeof(LevelMapTitleVisualGraphic));
            visualObject.transform.SetParent(stage, false);
            RectTransform visualRect =
                visualObject.GetComponent<RectTransform>();
            visualRect.anchorMin = Vector2.zero;
            visualRect.anchorMax = Vector2.one;
            visualRect.offsetMin = Vector2.zero;
            visualRect.offsetMax = Vector2.zero;
            LevelMapTitleVisualGraphic visual =
                visualObject.GetComponent<LevelMapTitleVisualGraphic>();
            visual.raycastTarget = false;

            var textObject = new GameObject(
                "LevelMapTitle",
                typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(stage, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.Load<Font>("Fonts/NotoSerifSC-Heavy") ??
                        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 50;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 34;
            text.resizeTextMaxSize = 50;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.94f, 0.88f, 0.72f, 1f);
            text.raycastTarget = false;
            text.text = title;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.015f, 0.035f, 0.04f, 0.94f);
            outline.effectDistance = new Vector2(1.35f, -1.35f);
            outline.useGraphicAlpha = true;

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(580f, 82f);
            rect.anchoredPosition = new Vector2(0f, 7f);

            _elapsed = isActiveAndEnabled ? 0f : PresentationDuration;
            ApplyPresentation();
            _root.SetActive(isActiveAndEnabled);
        }

        private void Update()
        {
            if (_root == null)
                return;

            if (Time.timeScale > 0f &&
                SceneTransitionRuntime.InteractionBarrier?.IsClear != false &&
                SceneTransitionRuntime.ExternalModalBarrier?.IsClear != false)
            {
                _elapsed = Mathf.Min(PresentationDuration,
                    _elapsed + Time.unscaledDeltaTime);
            }
            // Keep applying the layout after docking so changing resolution,
            // window size or safe area never leaves the locator off screen.
            ApplyPresentation();
        }

        private void ApplyPresentation()
        {
            if (_stage == null || _canvasGroup == null)
                return;

            float reveal = EaseOut(_elapsed / RevealDuration);
            float dock = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                (_elapsed - RevealDuration - ReadDuration) / DockDuration));
            Rect safeArea = NormalizedSafeArea();
            Camera stageCamera = Camera.main;
            float stageTop = stageCamera != null
                ? Mathf.Min(safeArea.yMax, stageCamera.rect.yMax)
                : safeArea.yMax;

            // The large reveal belongs to the camera stage. The compact title
            // belongs to the screen HUD, above the Tianji slip (top = 150).
            // Following the camera's letterbox inset after docking would push
            // the title onto that slip on 3:2 / 4:3 displays.
            Vector2 anchor = Vector2.Lerp(
                new Vector2(safeArea.center.x, stageTop),
                new Vector2(safeArea.xMin, safeArea.yMax), dock);
            _stage.anchorMin = _stage.anchorMax = anchor;
            _stage.anchoredPosition = Vector2.Lerp(
                new Vector2(0f, -12f),
                new Vector2(24f + StageWidth * DockScale * 0.5f, -24f),
                dock);
            float scale = Mathf.Lerp(
                Mathf.Lerp(0.965f, 1f, reveal), DockScale, dock);
            _stage.localScale = new Vector3(scale, scale, 1f);
            _canvasGroup.alpha = reveal;
        }

        private static float EaseOut(float progress)
        {
            return 1f - Mathf.Pow(1f - Mathf.Clamp01(progress), 3f);
        }

        private static Rect NormalizedSafeArea()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return new Rect(0f, 0f, 1f, 1f);

            Rect area = Screen.safeArea;
            if (area.width <= 0f || area.height <= 0f)
                return new Rect(0f, 0f, 1f, 1f);
            return Rect.MinMaxRect(
                Mathf.Clamp01(area.xMin / Screen.width),
                Mathf.Clamp01(area.yMin / Screen.height),
                Mathf.Clamp01(area.xMax / Screen.width),
                Mathf.Clamp01(area.yMax / Screen.height));
        }

        public void Close()
        {
            if (_root != null)
            {
                // Destroy is deferred; hide immediately so Show/Close/Show in
                // one frame cannot leave two visible titles.
                _root.SetActive(false);
                Destroy(_root);
            }
            _root = null;
            _stage = null;
            _canvasGroup = null;
            _title = null;
            _elapsed = 0f;
        }

        private void OnDisable()
        {
            // A retained map is deactivated while fighting. Preserve its title
            // but finish the intro so reactivation restores the compact HUD.
            _elapsed = PresentationDuration;
            if (_root != null)
                _root.SetActive(false);
        }

        private void OnEnable()
        {
            if (_root == null)
                return;

            ApplyPresentation();
            _root.SetActive(true);
        }

        private void OnDestroy()
        {
            Close();
        }
    }
}
