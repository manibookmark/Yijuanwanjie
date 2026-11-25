using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// Presents one level at a time after the portal journey has established its
    /// reading field. It owns only content, navigation, input shielding, and the
    /// short content reveal; the Effects module owns all transition imagery.
    /// Bootstrap may add more level descriptors without changing interaction.
    /// The class owns one cohesive final-screen lifecycle; any second screen or
    /// independent navigation mode must be split out before further growth.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class HubWorldSelectionPanel : MonoBehaviour, IScrollHandler
    {
        private static readonly Color PrimaryText =
            new Color(0.91f, 1f, 0.96f, 1f);
        private static readonly Color SecondaryText =
            new Color(0.68f, 0.84f, 0.80f, 1f);
        private static readonly Color Energy =
            new Color(0.43f, 1f, 0.91f, 1f);
        private static readonly Color HoverText =
            new Color(0.84f, 1f, 0.96f, 1f);
        private static readonly Color PressedText =
            new Color(0.96f, 1f, 1f, 1f);
        private static readonly Color Locked =
            new Color(0.46f, 0.54f, 0.53f, 1f);
        private readonly List<HubDestination> _destinations =
            new List<HubDestination>();
        private CanvasGroup _canvasGroup;
        private RectTransform _contentRoot;
        private CanvasGroup _contentGroup;
        private RectTransform _levelRoot;
        private CanvasGroup _levelGroup;
        private Text _title;
        private Text _description;
        private Text _pageIndicator;
        private Text _enterHint;
        private Text _leftArrow;
        private Text _rightArrow;
        private Button _leftZone;
        private Button _rightZone;
        private Button _enterZone;
        private Coroutine _showRoutine;
        private Coroutine _switchRoutine;
        private Coroutine _hideRoutine;
        private Font _titleFont;
        private Font _bodyFont;
        private int _currentIndex;
        public bool IsVisible => _canvasGroup != null && _canvasGroup.alpha > 0.01f;
        public event System.Action CloseRequested;
        public event System.Action<string> DestinationSelected;
        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            BuildView();
            HideImmediately();
        }
        private void Update()
        {
            if (!IsVisible || !_canvasGroup.interactable || _switchRoutine != null)
                return;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
                SwitchBy(-1);
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
                SwitchBy(1);
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                EnterCurrent();
            else if (Input.GetKeyDown(KeyCode.Escape))
                CloseRequested?.Invoke();
        }
        private void OnDisable()
        {
            StopAnimations();
        }
        /// <summary>Replaces the level carousel without changing its presentation.</summary>
        public void SetDestinations(IReadOnlyList<HubDestination> destinations)
        {
            _destinations.Clear();
            if (destinations != null)
            {
                for (int index = 0; index < destinations.Count; index++)
                    _destinations.Add(destinations[index]);
            }
            // 打开万界阵时默认落在当前已推进到的最深关区。
            // 目录按世界推进顺序维护，因此最后一个可用目标就是当前入口；
            // 尚未推进时自然回到第一个目标，避免面板永远固定在 G1。
            _currentIndex = FindDefaultDestinationIndex();
            RefreshCurrentDestination();
        }

        private int FindDefaultDestinationIndex()
        {
            if (_destinations.Count == 0)
                return 0;

            for (int index = _destinations.Count - 1; index >= 0; index--)
            {
                if (_destinations[index].Available)
                    return index;
            }
            return 0;
        }
        public void Show()
        {
            StopAnimations();
            _showRoutine = StartCoroutine(ShowRoutine());
        }

        /// <summary>Fades the panel out as the mirror of Show; re-entry safe.</summary>
        public void Hide()
        {
            if (_hideRoutine != null)
                return;
            StopAnimations();
            _hideRoutine = StartCoroutine(HideRoutine());
        }
        public void HideImmediately()
        {
            StopAnimations();
            if (_canvasGroup == null)
                return;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            if (_contentGroup != null)
            {
                _contentGroup.alpha = 0f;
                _contentGroup.interactable = false;
                _contentGroup.blocksRaycasts = false;
            }
            if (_contentRoot != null)
            {
                _contentRoot.anchoredPosition = new Vector2(0f, 24f);
                _contentRoot.localScale = new Vector3(0.95f, 0.97f, 1f);
            }
            if (_levelGroup != null)
                _levelGroup.alpha = 1f;
            if (_levelRoot != null)
                _levelRoot.anchoredPosition = Vector2.zero;
        }
        public void OnScroll(PointerEventData eventData)
        {
            if (!IsVisible || !_canvasGroup.interactable || eventData == null ||
                Mathf.Abs(eventData.scrollDelta.y) < 0.01f)
                return;
            SwitchBy(eventData.scrollDelta.y > 0f ? -1 : 1);
        }
        private IEnumerator ShowRoutine()
        {
            RefreshCurrentDestination();
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 1f;
            _contentGroup.alpha = 0f;
            _contentGroup.interactable = false;
            _contentGroup.blocksRaycasts = false;
            _contentRoot.anchoredPosition = new Vector2(0f, 24f);
            _contentRoot.localScale = new Vector3(0.95f, 0.97f, 1f);
            _levelGroup.alpha = 1f;
            _levelRoot.anchoredPosition = Vector2.zero;

            const float duration = 0.24f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float contentEased = EaseOutCubic(progress);
                _contentGroup.alpha = progress;
                _contentRoot.anchoredPosition = Vector2.LerpUnclamped(
                    new Vector2(0f, 24f),
                    Vector2.zero,
                    contentEased);
                _contentRoot.localScale = new Vector3(
                    Mathf.Lerp(0.95f, 1f, contentEased),
                    Mathf.Lerp(0.97f, 1f, contentEased),
                    1f);
                yield return null;
            }

            _canvasGroup.alpha = 1f;
            _contentGroup.alpha = 1f;
            _contentRoot.anchoredPosition = Vector2.zero;
            _contentRoot.localScale = Vector3.one;
            _contentGroup.interactable = true;
            _contentGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
            _showRoutine = null;
        }

        private IEnumerator HideRoutine()
        {
            _canvasGroup.interactable = false;
            _contentGroup.interactable = false;
            const float duration = 0.16f;
            float startAlpha = _contentGroup.alpha;
            Vector2 startPosition = _contentRoot.anchoredPosition;
            Vector3 startScale = _contentRoot.localScale;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = EaseOutCubic(progress);
                _contentGroup.alpha = Mathf.Lerp(startAlpha, 0f, progress * progress);
                _contentRoot.anchoredPosition = Vector2.LerpUnclamped(
                    startPosition,
                    new Vector2(0f, 24f),
                    eased);
                _contentRoot.localScale = new Vector3(
                    Mathf.Lerp(startScale.x, 0.95f, eased),
                    Mathf.Lerp(startScale.y, 0.97f, eased),
                    1f);
                yield return null;
            }

            _hideRoutine = null;
            HideImmediately();
        }

        private void SwitchBy(int direction)
        {
            if (_switchRoutine != null || _destinations.Count <= 1)
                return;

            int targetIndex = Mathf.Clamp(
                _currentIndex + direction,
                0,
                _destinations.Count - 1);
            if (targetIndex == _currentIndex)
                return;

            _switchRoutine = StartCoroutine(SwitchRoutine(targetIndex, direction));
        }

        private IEnumerator SwitchRoutine(int targetIndex, int direction)
        {
            _canvasGroup.interactable = false;
            const float halfDuration = 0.16f;
            float elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / halfDuration);
                _levelGroup.alpha = 1f - progress;
                _levelRoot.anchoredPosition = new Vector2(-direction * progress * 150f, 0f);
                yield return null;
            }

            _currentIndex = targetIndex;
            RefreshCurrentDestination();
            _levelRoot.anchoredPosition = new Vector2(direction * 150f, 0f);
            elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / halfDuration);
                float eased = EaseOutCubic(progress);
                _levelGroup.alpha = progress;
                _levelRoot.anchoredPosition = new Vector2(
                    direction * Mathf.Lerp(150f, 0f, eased),
                    0f);
                yield return null;
            }

            _levelGroup.alpha = 1f;
            _levelRoot.anchoredPosition = Vector2.zero;
            _canvasGroup.interactable = true;
            _switchRoutine = null;
        }

        private void EnterCurrent()
        {
            if (_destinations.Count == 0)
                return;

            HubDestination current = _destinations[_currentIndex];
            if (!current.Available)
                return;
            DestinationSelected?.Invoke(current.Id);
        }

        private void RefreshCurrentDestination()
        {
            if (_title == null)
                return;

            if (_destinations.Count == 0)
            {
                _title.text = "尚无显化关区";
                _description.text = string.Empty;
                _pageIndicator.text = string.Empty;
                _enterHint.text = string.Empty;
                SetButtonAvailability(_leftZone, false);
                SetButtonAvailability(_rightZone, false);
                SetButtonAvailability(_enterZone, false);
                _leftArrow.gameObject.SetActive(false);
                _rightArrow.gameObject.SetActive(false);
                return;
            }

            HubDestination current = _destinations[_currentIndex];
            _title.text = current.Title ?? string.Empty;
            _description.text = current.Subtitle ?? string.Empty;
            _title.color = current.Available ? PrimaryText : Locked;
            _description.color = current.Available ? SecondaryText : Locked;
            _pageIndicator.text = $"{_currentIndex + 1}/{_destinations.Count}";
            _enterHint.text = current.Available
                ? $"进入{current.Title}"
                : "封印未解";
            SetButtonAvailability(_enterZone, current.Available);

            bool hasPrevious = _currentIndex > 0;
            bool hasNext = _currentIndex < _destinations.Count - 1;
            SetButtonAvailability(_leftZone, hasPrevious);
            SetButtonAvailability(_rightZone, hasNext);
            _leftArrow.gameObject.SetActive(hasPrevious);
            _rightArrow.gameObject.SetActive(hasNext);
        }

        private void BuildView()
        {
            RectTransform root = GetComponent<RectTransform>();
            Stretch(root);

            RectTransform inputShield = CreateImage(
                "RealmSelectionInputShield",
                transform,
                new Color(1f, 1f, 1f, 0.001f));
            Stretch(inputShield);
            Image shieldImage = inputShield.GetComponent<Image>();
            shieldImage.raycastTarget = true;
            Button shieldButton = inputShield.gameObject.AddComponent<Button>();
            shieldButton.targetGraphic = shieldImage;
            shieldButton.transition = Selectable.Transition.None;
            // 点击关区内容之外的暗区等同 ESC / 返回。
            shieldButton.onClick.AddListener(() => CloseRequested?.Invoke());

            var contentObject = new GameObject(
                "LevelTextCondensation",
                typeof(RectTransform),
                typeof(CanvasGroup));
            contentObject.transform.SetParent(transform, false);
            _contentRoot = contentObject.GetComponent<RectTransform>();
            Stretch(_contentRoot);
            _contentGroup = contentObject.GetComponent<CanvasGroup>();
            _contentGroup.interactable = false;
            _contentGroup.blocksRaycasts = false;

            var levelObject = new GameObject(
                "CurrentLevelContent",
                typeof(RectTransform),
                typeof(CanvasGroup));
            levelObject.transform.SetParent(_contentRoot, false);
            _levelRoot = levelObject.GetComponent<RectTransform>();
            Place(_levelRoot, Vector2.zero, new Vector2(1400f, 760f));
            _levelGroup = levelObject.GetComponent<CanvasGroup>();

            _title = CreateText(
                _levelRoot,
                "LevelTitle",
                string.Empty,
                100,
                TextAnchor.MiddleCenter,
                PrimaryText,
                true);
            Place(_title.rectTransform, new Vector2(0f, 184f), new Vector2(1280f, 150f));

            HubEnergyParticleGraphic divider = CreateEnergyGraphic(
                "TitleEnergyDivider",
                _levelRoot);
            Place(
                divider.rectTransform,
                new Vector2(0f, 88f),
                new Vector2(300f, 24f));
            divider.ConfigureDivider(Energy, 56, 0.10f, 0.72f);

            _description = CreateText(
                _levelRoot,
                "LevelDescription",
                string.Empty,
                34,
                TextAnchor.UpperCenter,
                SecondaryText,
                false);
            _description.lineSpacing = 1.38f;
            Place(
                _description.rectTransform,
                new Vector2(0f, -18f),
                new Vector2(1180f, 170f));

            _enterZone = CreateEnergyButton(
                "EnterCurrentLevel",
                "点击进入",
                _levelRoot,
                new Vector2(0f, -220f),
                new Vector2(840f, 120f),
                30,
                168,
                0.34f,
                1.08f,
                out _enterHint);
            _enterZone.onClick.AddListener(EnterCurrent);

            _pageIndicator = CreateText(
                _contentRoot,
                "LevelPageIndicator",
                string.Empty,
                28,
                TextAnchor.MiddleLeft,
                new Color(SecondaryText.r, SecondaryText.g, SecondaryText.b, 0.82f),
                false);
            Place(
                _pageIndicator.rectTransform,
                new Vector2(-820f, 455f),
                new Vector2(160f, 64f));

            // Navigation occupies only two deliberate side rails. It never
            // overlaps level content or the top-right return control.
            _leftZone = CreateInvisibleButton("PreviousLevelZone", _contentRoot);
            Place(
                _leftZone.image.rectTransform,
                new Vector2(-820f, 0f),
                new Vector2(180f, 420f));
            _leftZone.onClick.AddListener(() => SwitchBy(-1));

            _rightZone = CreateInvisibleButton("NextLevelZone", _contentRoot);
            Place(
                _rightZone.image.rectTransform,
                new Vector2(820f, 0f),
                new Vector2(180f, 420f));
            _rightZone.onClick.AddListener(() => SwitchBy(1));

            _leftArrow = CreateText(
                _leftZone.transform,
                "PreviousLevelArrow",
                "‹",
                96,
                TextAnchor.MiddleCenter,
                new Color(PrimaryText.r, PrimaryText.g, PrimaryText.b, 0.56f),
                true);
            Stretch(_leftArrow.rectTransform);

            _rightArrow = CreateText(
                _rightZone.transform,
                "NextLevelArrow",
                "›",
                96,
                TextAnchor.MiddleCenter,
                new Color(PrimaryText.r, PrimaryText.g, PrimaryText.b, 0.56f),
                true);
            Stretch(_rightArrow.rectTransform);

            Button close = CreateEnergyButton(
                "ReturnToHub",
                "返回",
                _contentRoot,
                new Vector2(820f, 455f),
                new Vector2(180f, 72f),
                26,
                80,
                0.24f,
                0.94f,
                out _);
            close.onClick.AddListener(() => CloseRequested?.Invoke());
        }

        private static HubEnergyParticleGraphic CreateEnergyGraphic(
            string objectName,
            Transform parent)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(HubEnergyParticleGraphic));
            go.transform.SetParent(parent, false);
            return go.GetComponent<HubEnergyParticleGraphic>();
        }

        private Button CreateEnergyButton(
            string objectName,
            string label,
            Transform parent,
            Vector2 position,
            Vector2 size,
            int fontSize,
            int particleCount,
            float idleIntensity,
            float hoverIntensity,
            out Text labelText)
        {
            HubEnergyParticleGraphic energy = CreateEnergyGraphic(
                objectName,
                parent);
            Place(energy.rectTransform, position, size);
            energy.ConfigureButtonFrame(
                Energy,
                particleCount,
                0.10f,
                idleIntensity,
                hoverIntensity);

            Button button = energy.gameObject.AddComponent<Button>();
            button.targetGraphic = energy;
            button.transition = Selectable.Transition.None;

            labelText = CreateText(
                energy.transform,
                "Label",
                label,
                fontSize,
                TextAnchor.MiddleCenter,
                Energy,
                false);
            Stretch(labelText.rectTransform);
            energy.BindInteraction(
                button,
                labelText,
                Energy,
                HoverText,
                PressedText,
                Locked);
            return button;
        }

        private static void SetButtonAvailability(Button button, bool available)
        {
            if (button == null)
                return;
            button.interactable = available;
            if (button.targetGraphic is HubEnergyParticleGraphic energy)
                energy.SetInteractionEnabled(available);
            else if (button.image != null)
                button.image.raycastTarget = available;
            else if (button.targetGraphic != null)
                button.targetGraphic.raycastTarget = available;
        }

        private Button CreateInvisibleButton(string objectName, Transform parent)
        {
            RectTransform rect = CreateImage(
                objectName,
                parent,
                new Color(1f, 1f, 1f, 0.001f));
            Image image = rect.GetComponent<Image>();
            image.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            return button;
        }

        private Text CreateText(
            Transform parent,
            string objectName,
            string value,
            int size,
            TextAnchor alignment,
            Color colour,
            bool titleFont)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = titleFont ? TitleFont : BodyFont;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = colour;
            text.text = value ?? string.Empty;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static RectTransform CreateImage(
            string objectName,
            Transform parent,
            Color colour)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image.rectTransform;
        }

        private void StopAnimations()
        {
            if (_showRoutine != null)
            {
                StopCoroutine(_showRoutine);
                _showRoutine = null;
            }
            if (_switchRoutine != null)
            {
                StopCoroutine(_switchRoutine);
                _switchRoutine = null;
            }
            if (_hideRoutine != null)
            {
                StopCoroutine(_hideRoutine);
                _hideRoutine = null;
            }
        }

        private Font TitleFont => _titleFont != null
            ? _titleFont
            : (_titleFont = Resources.Load<Font>("Fonts/NotoSerifSC-Heavy")
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        private Font BodyFont => _bodyFont != null
            ? _bodyFont
            : (_bodyFont = Resources.Load<Font>("Fonts/NotoSerifSC-Medium")
                ?? TitleFont);

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static float EaseOutCubic(float progress)
        {
            return 1f - Mathf.Pow(1f - progress, 3f);
        }
    }
}
