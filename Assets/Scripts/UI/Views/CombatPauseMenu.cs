using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Components;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 地图与战斗共用的 Esc 菜单。
    ///
    /// 这里保留输入、遮挡和暂停恢复的唯一所有权，面板外观复用卷宗的
    /// 玉墨色板式。退出游戏必须先经过确认层，避免误触直接离开当前行程。
    /// </summary>
    public sealed class CombatPauseMenu : MonoBehaviour
    {
        public event Action ReturnToHubRequested;
        public event Action QuitRequested;
        public event Action SkipRequested;
        public event Action<bool> OpenChanged;

        private CanvasGroup _group;
        private GameObject _mainContent;
        private GameObject _confirmContent;
        private Button _resumeButton;
        private Button _cancelQuitButton;
        private Text _errorText;
        private bool _open;
        private bool _confirmingQuit;
        private bool _quitDispatching;
        private IDisposable _pauseLease;
        private bool _cinematic;

        public bool IsOpen => _open;

        public static CombatPauseMenu Create(Transform owner)
            => Create(owner, false);

        public static CombatPauseMenu CreateForCinematic(Transform owner)
            => Create(owner, true);

        private static CombatPauseMenu Create(Transform owner, bool cinematic)
        {
            var canvasObject = new GameObject(
                "PauseMenuCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(owner, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var menuObject = new GameObject(
                "CombatPauseMenu", typeof(RectTransform));
            menuObject.transform.SetParent(canvasObject.transform, false);
            CombatPauseMenu menu = menuObject.AddComponent<CombatPauseMenu>();
            menu._cinematic = cinematic;
            menu.Build(canvasObject.transform);
            return menu;
        }

        public void Build(Transform canvasRoot)
        {
            if (_group != null)
                return;

            transform.SetParent(canvasRoot, false);
            RectTransform root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;

            _group = gameObject.AddComponent<CanvasGroup>();

            BuildScrim();

            GameObject panel = new GameObject(
                "JadePausePanel",
                typeof(RectTransform),
                typeof(Image));
            panel.transform.SetParent(transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(720f, 560f);
            BuildPanelShell(panelRect, panel.GetComponent<Image>());
            BuildHeader(panel.transform);

            _mainContent = CreateContentRoot(panel.transform, "MainActions");
            _resumeButton = CreateActionButton(
                _mainContent.transform,
                "ResumeButton",
                _cinematic ? "继续播放" : "继续游戏",
                primary: true,
                danger: false,
                () => Close());
            CreateActionButton(
                _mainContent.transform,
                "ReturnButton",
                _cinematic ? "跳过剧情" : "返回仙域",
                primary: false,
                danger: false,
                () =>
                {
                    if (_cinematic) SkipRequested?.Invoke();
                    else ReturnToHubRequested?.Invoke();
                });
            CreateActionButton(
                _mainContent.transform,
                "QuitButton",
                "退出游戏",
                primary: false,
                danger: true,
                RequestQuitConfirmation);

            _confirmContent = CreateContentRoot(panel.transform, "QuitConfirmation");
            _confirmContent.SetActive(false);
            BuildQuitConfirmation(_confirmContent.transform);

            _errorText = JadePanelView.CreateText(
                "ErrorMessage",
                panel.transform,
                string.Empty,
                15,
                JadePanelView.Danger,
                TextAnchor.MiddleCenter);
            _errorText.raycastTarget = false;
            _errorText.verticalOverflow = VerticalWrapMode.Truncate;
            _errorText.resizeTextForBestFit = true;
            _errorText.resizeTextMinSize = 12;
            _errorText.resizeTextMaxSize = 15;
            RectTransform hintRect = _errorText.rectTransform;
            hintRect.anchorMin = new Vector2(0.5f, 0f);
            hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            // Keep save failures in a reserved footer, outside either action layout.
            hintRect.sizeDelta = new Vector2(600f, 56f);
            hintRect.anchoredPosition = new Vector2(0f, 16f);

            Close();
        }

        /// <summary>
        /// 让失败的保存/回滚反馈留在暂停层，不把玩家误送回一个已恢复时间流的场景。
        /// </summary>
        public void ShowError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (_errorText != null)
                _errorText.text = message;
        }

        /// <summary>
        /// 由地图/战斗 Bootstrap 共用，保证编辑器里“退出游戏”也真正结束 Play。
        /// </summary>
        public static void RequestApplicationQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BuildScrim()
        {
            GameObject scrim = new GameObject(
                "Scrim", typeof(RectTransform), typeof(Image));
            scrim.transform.SetParent(transform, false);
            RectTransform scrimRect = scrim.GetComponent<RectTransform>();
            scrimRect.anchorMin = Vector2.zero;
            scrimRect.anchorMax = Vector2.one;
            scrimRect.offsetMin = scrimRect.offsetMax = Vector2.zero;

            Image image = scrim.GetComponent<Image>();
            image.color = new Color(
                JadePanelView.Ink.r,
                JadePanelView.Ink.g,
                JadePanelView.Ink.b,
                0.84f);
            image.raycastTarget = true;
        }

        private static void BuildPanelShell(RectTransform panel, Image background)
        {
            GameObject shadow = new GameObject("PanelShadow", typeof(Image));
            shadow.transform.SetParent(panel, false);
            RectTransform shadowRect = shadow.GetComponent<RectTransform>();
            JadePanelView.Stretch(
                shadowRect,
                new Vector2(-18f, -22f),
                new Vector2(18f, 14f));
            Image shadowImage = shadow.GetComponent<Image>();
            shadowImage.color = new Color(0f, 0f, 0f, 0.48f);
            shadowImage.raycastTarget = false;

            background.color = JadePanelView.Ink;
            background.raycastTarget = true;
            Outline outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(
                JadePanelView.Jade.r,
                JadePanelView.Jade.g,
                JadePanelView.Jade.b,
                0.70f);
            outline.effectDistance = new Vector2(1f, -1f);

            GameObject surface = new GameObject(
                "InnerSurface", typeof(Image), typeof(Outline));
            surface.transform.SetParent(panel, false);
            RectTransform surfaceRect = surface.GetComponent<RectTransform>();
            JadePanelView.Stretch(
                surfaceRect,
                new Vector2(12f, 12f),
                new Vector2(-12f, -12f));
            Image surfaceImage = surface.GetComponent<Image>();
            surfaceImage.color = JadePanelView.Surface;
            surfaceImage.raycastTarget = false;
            Outline surfaceOutline = surface.GetComponent<Outline>();
            surfaceOutline.effectColor = new Color(
                JadePanelView.Jade.r,
                JadePanelView.Jade.g,
                JadePanelView.Jade.b,
                0.18f);
            surfaceOutline.effectDistance = new Vector2(1f, -1f);

            GameObject headerWash = new GameObject("HeaderWash", typeof(Image));
            headerWash.transform.SetParent(panel, false);
            RectTransform headerRect = headerWash.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(13f, -122f);
            headerRect.offsetMax = new Vector2(-13f, -13f);
            Image headerImage = headerWash.GetComponent<Image>();
            headerImage.color = new Color(
                JadePanelView.SurfaceRaised.r,
                JadePanelView.SurfaceRaised.g,
                JadePanelView.SurfaceRaised.b,
                0.42f);
            headerImage.raycastTarget = false;
        }

        private void BuildHeader(Transform panel)
        {
            Text title = JadePanelView.CreateText(
                "Title",
                panel,
                "行止",
                42,
                JadePanelView.TextPrimary,
                TextAnchor.MiddleCenter,
                heavy: true);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(640f, 58f);
            titleRect.anchoredPosition = new Vector2(0f, -26f);

            Text subtitle = JadePanelView.CreateText(
                "Subtitle",
                panel,
                _cinematic ? "剧情暂歇" : "战斗暂歇 · 当前行程已保留",
                17,
                JadePanelView.TextSecondary,
                TextAnchor.MiddleCenter);
            RectTransform subtitleRect = subtitle.rectTransform;
            subtitleRect.anchorMin = new Vector2(0.5f, 1f);
            subtitleRect.anchorMax = new Vector2(0.5f, 1f);
            subtitleRect.pivot = new Vector2(0.5f, 1f);
            subtitleRect.sizeDelta = new Vector2(640f, 30f);
            subtitleRect.anchoredPosition = new Vector2(0f, -78f);

            GameObject divider = new GameObject("HeaderDivider", typeof(Image));
            divider.transform.SetParent(panel, false);
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 1f);
            dividerRect.anchorMax = new Vector2(1f, 1f);
            dividerRect.pivot = new Vector2(0.5f, 1f);
            dividerRect.offsetMin = new Vector2(52f, -122f);
            dividerRect.offsetMax = new Vector2(-52f, -120f);
            Image line = divider.GetComponent<Image>();
            line.color = new Color(
                JadePanelView.Jade.r,
                JadePanelView.Jade.g,
                JadePanelView.Jade.b,
                0.48f);
            line.raycastTarget = false;

            GameObject seal = new GameObject("DividerSeal", typeof(Image));
            seal.transform.SetParent(divider.transform, false);
            RectTransform sealRect = seal.GetComponent<RectTransform>();
            sealRect.anchorMin = new Vector2(0f, 0.5f);
            sealRect.anchorMax = new Vector2(0f, 0.5f);
            sealRect.pivot = new Vector2(0.5f, 0.5f);
            sealRect.sizeDelta = new Vector2(10f, 10f);
            sealRect.anchoredPosition = Vector2.zero;
            sealRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image sealImage = seal.GetComponent<Image>();
            sealImage.color = JadePanelView.Gold;
            sealImage.raycastTarget = false;
        }

        private static GameObject CreateContentRoot(Transform panel, string name)
        {
            GameObject content = new GameObject(
                name,
                typeof(RectTransform),
                typeof(VerticalLayoutGroup));
            content.transform.SetParent(panel, false);
            RectTransform rect = content.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(74f, 86f);
            rect.offsetMax = new Vector2(-74f, -150f);

            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return content;
        }

        private void BuildQuitConfirmation(Transform content)
        {
            Text title = JadePanelView.CreateText(
                "ConfirmTitle",
                content,
                "确认退出游戏？",
                28,
                JadePanelView.TextPrimary,
                TextAnchor.MiddleCenter,
                heavy: true);
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 46f;
            titleLayout.flexibleWidth = 1f;

            _cancelQuitButton = CreateActionButton(
                content,
                "CancelQuitButton",
                _cinematic ? "继续播放" : "继续游戏",
                primary: true,
                danger: false,
                Close);
            CreateActionButton(
                content,
                "ConfirmQuitButton",
                "确认退出",
                primary: false,
                danger: true,
                ConfirmQuit);
        }

        private static Button CreateActionButton(
            Transform parent,
            string name,
            string label,
            bool primary,
            bool danger,
            Action action)
        {
            Button button = JadePanelView.CreateTextButton(
                name,
                parent,
                label,
                new Vector2(1f, 64f),
                primary,
                danger);
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 64f;
            layout.preferredHeight = 64f;
            layout.flexibleWidth = 1f;
            button.onClick.AddListener(() => action?.Invoke());
            return button;
        }

        private void RequestQuitConfirmation()
        {
            if (!_open || _quitDispatching)
                return;

            _confirmingQuit = true;
            _mainContent?.SetActive(false);
            _confirmContent?.SetActive(true);
            ClearHint();
            Select(_cancelQuitButton);
        }

        private void CancelQuitConfirmation()
        {
            _confirmingQuit = false;
            _confirmContent?.SetActive(false);
            _mainContent?.SetActive(true);
            ClearHint();
            Select(_resumeButton);
        }

        private void ConfirmQuit()
        {
            if (!_open || !_confirmingQuit || _quitDispatching)
                return;

            _quitDispatching = true;
            try
            {
                // 只有确认按钮会发出事件；Bootstrap 成功保存/回滚后负责 Close。
                QuitRequested?.Invoke();
            }
            finally
            {
                _quitDispatching = false;
            }
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape))
                return;

            if (!_open)
            {
                Open();
                return;
            }

            if (_confirmingQuit)
                CancelQuitConfirmation();
            else
                Close();
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnDestroy() => Close();

        public void Open()
        {
            if (_open || _group == null)
                return;

            _pauseLease = PresentationTimeScale.Shared.Hold(0f);
            _open = true;
            _confirmingQuit = false;
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _confirmContent?.SetActive(false);
            _mainContent?.SetActive(true);
            ClearHint();
            Select(_resumeButton);
            OpenChanged?.Invoke(true);
        }

        public void Close()
        {
            bool wasOpen = _open;
            _open = false;
            _confirmingQuit = false;
            IDisposable lease = _pauseLease;
            _pauseLease = null;
            lease?.Dispose();
            if (_group == null)
            {
                if (wasOpen) OpenChanged?.Invoke(false);
                return;
            }

            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _confirmContent?.SetActive(false);
            _mainContent?.SetActive(true);

            if (EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
            if (wasOpen) OpenChanged?.Invoke(false);
        }

        private void ClearHint()
        {
            if (_errorText != null)
                _errorText.text = string.Empty;
        }

        private static void Select(Selectable selectable)
        {
            if (selectable == null || EventSystem.current == null)
                return;
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
            selectable.Select();
        }
    }
}
