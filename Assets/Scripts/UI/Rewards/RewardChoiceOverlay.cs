using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 通用奖励候选面板；空白区不关闭，只响应候选项和已提供工具动作。
    ///
    /// 规模审查：本文件超过 500 行，但候选卡面、工具栏、入场动画和点击门禁
    /// 仍共享同一个选择面板生命周期。灵印纸面与 Tooltip 由 RewardSealChoiceView
    /// 独立拥有，本面板只装配同尺寸候选，不引入宿主选择或奖励规则状态；
    /// 卡牌选项的上下文提示只负责解释本次选择收益，不改卡牌机制文字；
    /// 若未来加入候选生成、业务提交或第二种独立选择流程，再重新拆分状态所有者。
    /// </summary>
    public sealed class RewardChoiceOverlay : MonoBehaviour
    {
        public GameObject Root { get; private set; }
        public bool IsShown => Root != null;
        public IReadOnlyList<Button> ChoiceButtons => _choiceButtons;
        public IReadOnlyList<Button> UtilityButtons => _utilityButtons;
        public RectTransform ChoiceArea { get; private set; }

        private readonly List<Button> _choiceButtons = new List<Button>();
        private readonly List<Button> _utilityButtons = new List<Button>();
        private readonly List<RectTransform> _choiceSlots =
            new List<RectTransform>();
        private readonly List<Vector2> _choiceRestPositions = new List<Vector2>();
        private CanvasGroup _rootGroup;
        private RectTransform _panel;
        private RectTransform _breakthroughHalo;
        private RectTransform _titleSheen;
        private Coroutine _animation;
        private bool _isClosing;
        private CardTooltipView _cardTooltip;
        private bool _suspended;
        private int _viewGeneration;
        private int _closingSelectedIndex = -1;
        private Action _closeCompletion;

        public void Show(
            Canvas canvas,
            string title,
            IReadOnlyList<RewardFeedbackItem> items,
            IReadOnlyList<RewardUtilityAction> actions,
            Action<int> onSelected,
            RewardCardTooltipContext cardContext = null)
        {
            Close();
            if (canvas == null)
                throw new ArgumentNullException(nameof(canvas));
            if (items == null || items.Count == 0)
                throw new ArgumentException(
                    "Reward choice requires at least one item.",
                    nameof(items));
            Root = RewardPanelFactory.CreateRoot(
                canvas,
                "RewardChoiceOverlay",
                addDismissButton: false,
                out CanvasGroup rootGroup,
                out _);
            _rootGroup = rootGroup;
            int generation = _viewGeneration;
            GameObject shownRoot = Root;
            var tooltipObject = new GameObject("RewardCardTooltip", typeof(RectTransform));
            tooltipObject.transform.SetParent(Root.transform, false);
            _cardTooltip = tooltipObject.AddComponent<CardTooltipView>();
            int actionCount = CountActions(actions);
            bool hasActions = actionCount > 0;
            float contentWidth = items.Count switch
            {
                1 => 720f,
                2 => 1060f,
                _ => 1420f
            };
            RectTransform panel = RewardPanelFactory.CreatePanel(
                Root.transform,
                "LargeChoicePanel",
                new Vector2(contentWidth + (hasActions ? 210f : 0f), 760f),
                out _);
            _panel = panel;

            Text titleText = RewardPanelFactory.CreateText(
                panel,
                "Title",
                title,
                38,
                TextAnchor.MiddleCenter,
                RewardPanelFactory.PrimaryText);
            RectTransform titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0.08f, 0.86f);
            titleRect.anchorMax = new Vector2(
                hasActions ? 0.82f : 0.92f,
                0.97f);
            titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;
            CreateEntranceAccents(panel, titleRect);

            ChoiceArea = CreateRect(
                panel,
                "ChoiceItems",
                new Vector2(0.05f, 0.08f),
                new Vector2(hasActions ? 0.82f : 0.95f, 0.85f));
            var layout = ChoiceArea.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 26f;
            layout.padding = new RectOffset(18, 18, 8, 8);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            for (int i = 0; i < items.Count; i++)
            {
                int capturedIndex = i;
                Button button = CreateChoice(
                    ChoiceArea,
                    items[i],
                    capturedIndex,
                    out RectTransform slot,
                    _cardTooltip,
                    cardContext);
                button.onClick.AddListener(
                    () =>
                    {
                        if (CanSelect(generation, shownRoot))
                            onSelected?.Invoke(capturedIndex);
                    });
                _choiceButtons.Add(button);
                _choiceSlots.Add(slot);
            }

            if (hasActions)
            {
                RectTransform rail = CreateRect(
                    panel,
                    "UtilityActionRail",
                    new Vector2(0.84f, 0.12f),
                    new Vector2(0.97f, 0.88f));
                var railLayout =
                    rail.gameObject.AddComponent<VerticalLayoutGroup>();
                railLayout.spacing = 18f;
                railLayout.padding = new RectOffset(8, 8, 16, 16);
                railLayout.childAlignment = TextAnchor.UpperCenter;
                railLayout.childControlWidth = true;
                railLayout.childControlHeight = false;
                railLayout.childForceExpandWidth = true;
                railLayout.childForceExpandHeight = false;

                for (int i = 0; i < actions.Count; i++)
                {
                    RewardUtilityAction action = actions[i];
                    if (action == null || action.Execute == null)
                        continue;
                    Button button = RewardPanelFactory.CreateButton(
                        rail,
                        "Utility_" + action.Kind,
                        action.Label,
                        RewardPanelFactory.ResolveUtilityAccent(action.Kind));
                    RewardUtilityAction capturedAction = action;
                    button.onClick.AddListener(
                        () =>
                        {
                            if (CanSelect(generation, shownRoot))
                                capturedAction.Execute();
                        });
                    _utilityButtons.Add(button);
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(ChoiceArea);
            foreach (RectTransform slot in _choiceSlots)
                _choiceRestPositions.Add(slot != null ? slot.anchoredPosition : Vector2.zero);
            SetButtonsInteractable(false);
            ResumePresentation();
        }

        /// <summary>
        /// Locks all input, emphasizes the selected ordinary reward, then
        /// closes the panel. Completion runs only after the visible panel has
        /// left the screen, making it a reliable presentation checkpoint.
        /// </summary>
        public bool PlaySelectionAndClose(
            int selectedIndex,
            Action onComplete)
        {
            if (Root == null || _isClosing || selectedIndex < 0 ||
                selectedIndex >= _choiceSlots.Count)
                return false;

            BeginAnimatedClose(selectedIndex, onComplete);
            return true;
        }

        /// <summary>Animated close for a utility action such as cash-out.</summary>
        public bool PlayClose(Action onComplete)
        {
            if (Root == null || _isClosing)
                return false;
            BeginAnimatedClose(-1, onComplete);
            return true;
        }

        private void BeginAnimatedClose(
            int selectedIndex,
            Action onComplete)
        {
            _isClosing = true;
            _closingSelectedIndex = selectedIndex;
            _closeCompletion = onComplete;
            _cardTooltip?.Hide();
            SetButtonsInteractable(false);
            StopAnimation();
            if (isActiveAndEnabled && Root.activeInHierarchy)
                _animation = StartCoroutine(AnimateSelectionAndClose(
                    selectedIndex, _viewGeneration, Root));
        }

        public void Close()
        {
            StopAnimation();
            DestroyRootState();
        }

        private void StopAnimation()
        {
            if (_animation != null) StopCoroutine(_animation);
            _animation = null;
        }

        private bool CanSelect(int generation, GameObject root)
            => generation == _viewGeneration && Root == root &&
               isActiveAndEnabled && Root != null && Root.activeInHierarchy && !_isClosing;

        private void ResumePresentation()
        {
            if (Root == null) return;
            if (!isActiveAndEnabled)
            {
                SuspendPresentation();
                return;
            }
            StopAnimation();
            _suspended = false;
            Root.SetActive(true);
            _rootGroup.alpha = 1f;
            _rootGroup.interactable = _rootGroup.blocksRaycasts = true;
            _panel.localScale = Vector3.one;
            for (int i = 0; i < _choiceSlots.Count; i++)
            {
                RectTransform slot = _choiceSlots[i];
                if (slot == null) continue;
                slot.anchoredPosition = _choiceRestPositions[i];
                slot.localScale = Vector3.one;
                CanvasGroup group = slot.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1f;
            }
            SetButtonsInteractable(false);
            _animation = _isClosing
                ? StartCoroutine(AnimateSelectionAndClose(_closingSelectedIndex, _viewGeneration, Root))
                : StartCoroutine(AnimateEnhancedEntrance());
        }

        private void SuspendPresentation()
        {
            if (Root == null) return;
            StopAnimation();
            _cardTooltip?.Hide();
            _suspended = true;
            _rootGroup.interactable = _rootGroup.blocksRaycasts = false;
            Root.SetActive(false);
        }

        private static Button CreateChoice(
            RectTransform parent,
            RewardFeedbackItem item,
            int index,
            out RectTransform slotRect,
            CardTooltipView tooltip,
            RewardCardTooltipContext cardContext)
        {
            var slotGo = new GameObject(
                $"Choice_{index}",
                typeof(RectTransform),
                typeof(LayoutElement));
            slotGo.transform.SetParent(parent, false);
            LayoutElement slotLayout = slotGo.GetComponent<LayoutElement>();
            slotLayout.minWidth = 230f;
            slotLayout.minHeight = 520f;
            slotLayout.preferredWidth = 300f;
            slotLayout.preferredHeight = 540f;
            slotLayout.flexibleWidth = 0f;
            slotLayout.flexibleHeight = 0f;
            slotRect = slotGo.GetComponent<RectTransform>();
            slotRect.sizeDelta = new Vector2(300f, 540f);
            slotGo.AddComponent<CanvasGroup>();

            CardPresentationProfile profile = CardPresentationProfile.LoadDefault();
            float presentationScale = Mathf.Min(
                285f / Mathf.Max(1f, profile.referenceSize.x),
                520f / Mathf.Max(1f, profile.referenceSize.y));
            if (item != null && item.Kind == RewardFeedbackKind.Seal)
                return RewardSealChoiceView.Create(
                    slotGo.transform, item, tooltip, profile, presentationScale);

            if (item != null &&
                item.Kind == RewardFeedbackKind.Card &&
                item.Card != null)
            {
                CardView card = CardViewFactory.Create(
                    slotGo.transform,
                    profile,
                    addHoverHandler: false);
                RectTransform cardRect = card.transform as RectTransform;
                bool hasDecisionHint = !string.IsNullOrWhiteSpace(item.DecisionHint);
                cardRect.anchorMin = cardRect.anchorMax =
                    new Vector2(0.5f, 0.5f);
                cardRect.anchoredPosition = hasDecisionHint ? new Vector2(0f, 35f) : Vector2.zero;
                float cardScale = hasDecisionHint
                    ? Mathf.Min(presentationScale, 440f / Mathf.Max(1f, profile.referenceSize.y))
                    : presentationScale;
                cardRect.localScale = Vector3.one * cardScale;
                card.UpdateCard(
                    CardDisplayData.FromCard(item.Card, canPlay: true),
                    index);
                RewardCardHoverMotion motion =
                    card.gameObject.AddComponent<RewardCardHoverMotion>();
                motion.Bind(card);
                card.gameObject.AddComponent<RewardCardTooltipPresenter>()
                    .Bind(item.Card, tooltip, cardContext);
                if (hasDecisionHint)
                {
                    Text hint = RewardPanelFactory.CreateText(slotGo.transform,
                        "ChoiceDecisionHint", item.DecisionHint, 21,
                        TextAnchor.MiddleCenter, RewardPanelFactory.SecondaryText);
                    hint.raycastTarget = false;
                    RectTransform hintRect = hint.rectTransform;
                    hintRect.anchorMin = new Vector2(.05f, 0f);
                    hintRect.anchorMax = new Vector2(.95f, 0f);
                    hintRect.pivot = new Vector2(.5f, 0f);
                    hintRect.anchoredPosition = new Vector2(0f, 8f);
                    hintRect.sizeDelta = new Vector2(0f, 66f);
                }
                return card.cardButton;
            }

            var tile = new GameObject(
                "RewardTile",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            tile.transform.SetParent(slotGo.transform, false);
            RectTransform tileRect = tile.GetComponent<RectTransform>();
            RewardPanelFactory.Stretch(tileRect, 6f);
            Image image = tile.GetComponent<Image>();
            Color accent = item != null ? item.Accent : Color.gray;
            image.color = new Color(
                accent.r * 0.20f,
                accent.g * 0.20f,
                accent.b * 0.20f,
                0.92f);
            Button button = tile.GetComponent<Button>();
            button.targetGraphic = image;

            bool hasArtwork = item?.Artwork != null;
            if (hasArtwork)
            {
                var artworkObject = new GameObject(
                    "RewardArtwork",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                artworkObject.transform.SetParent(tile.transform, false);
                Image artwork = artworkObject.GetComponent<Image>();
                artwork.sprite = item.Artwork;
                artwork.preserveAspect = true;
                artwork.raycastTarget = false;
                RectTransform artworkRect = artwork.rectTransform;
                artworkRect.anchorMin = artworkRect.anchorMax =
                    new Vector2(0.5f, 0.67f);
                artworkRect.pivot = new Vector2(0.5f, 0.5f);
                artworkRect.sizeDelta = new Vector2(236f, 236f);
            }

            Text name = RewardPanelFactory.CreateText(
                tile.transform,
                "Name",
                item?.DisplayName,
                30,
                TextAnchor.MiddleCenter,
                RewardPanelFactory.PrimaryText);
            RectTransform nameRect = name.rectTransform;
            nameRect.anchorMin = new Vector2(
                0.08f,
                hasArtwork ? 0.34f : 0.56f);
            nameRect.anchorMax = new Vector2(
                0.92f,
                hasArtwork ? 0.45f : 0.84f);
            nameRect.offsetMin = nameRect.offsetMax = Vector2.zero;
            Text desc = RewardPanelFactory.CreateText(
                tile.transform,
                "Description",
                item?.Description,
                21,
                TextAnchor.UpperCenter,
                RewardPanelFactory.SecondaryText);
            RectTransform descRect = desc.rectTransform;
            descRect.anchorMin = new Vector2(0.10f, 0.10f);
            descRect.anchorMax = new Vector2(
                0.90f,
                hasArtwork ? 0.32f : 0.53f);
            descRect.offsetMin = descRect.offsetMax = Vector2.zero;
            return button;
        }

        private IEnumerator AnimateSelectionAndClose(
            int selectedIndex,
            int generation,
            GameObject root)
        {
            RectTransform selected = selectedIndex >= 0 &&
                selectedIndex < _choiceSlots.Count
                    ? _choiceSlots[selectedIndex]
                    : null;
            Vector3 panelStart = _panel != null
                ? _panel.localScale
                : Vector3.one;
            Vector3 selectedStart = selected != null
                ? selected.localScale
                : Vector3.one;

            float focusDuration = selected != null ? 0.16f : 0.08f;
            float elapsed = 0f;
            while (elapsed < focusDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / focusDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                for (int i = 0; i < _choiceSlots.Count; i++)
                {
                    RectTransform slot = _choiceSlots[i];
                    if (slot == null) continue;
                    CanvasGroup group = slot.GetComponent<CanvasGroup>();
                    if (group != null)
                    {
                        float targetAlpha = i == selectedIndex
                            ? 1f
                            : selectedIndex >= 0 ? 0.22f : 0.55f;
                        group.alpha = Mathf.Lerp(1f, targetAlpha, eased);
                    }
                }
                if (selected != null)
                {
                    selected.localScale = Vector3.LerpUnclamped(
                        selectedStart,
                        selectedStart * 1.075f,
                        eased);
                }
                yield return null;
            }

            float closeDuration = 0.20f;
            elapsed = 0f;
            float startAlpha = _rootGroup != null
                ? _rootGroup.alpha
                : 1f;
            while (elapsed < closeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / closeDuration);
                float eased = t * t;
                if (_rootGroup != null)
                    _rootGroup.alpha = Mathf.Lerp(startAlpha, 0f, eased);
                if (_panel != null)
                {
                    _panel.localScale = Vector3.LerpUnclamped(
                        panelStart,
                        panelStart * 0.93f,
                        eased);
                }
                if (selected != null)
                {
                    selected.localScale = Vector3.LerpUnclamped(
                        selectedStart * 1.075f,
                        selectedStart * 0.90f,
                        eased);
                }
                yield return null;
            }

            if (generation != _viewGeneration || Root != root) yield break;
            _animation = null;
            Action completion = _closeCompletion;
            _closeCompletion = null;
            DestroyRootState();
            completion?.Invoke();
        }

        private void SetButtonsInteractable(bool interactable)
        {
            for (int i = 0; i < _choiceButtons.Count; i++)
                if (_choiceButtons[i] != null)
                    _choiceButtons[i].interactable = interactable;
            for (int i = 0; i < _utilityButtons.Count; i++)
                if (_utilityButtons[i] != null)
                    _utilityButtons[i].interactable = interactable;
        }

        private void CreateEntranceAccents(
            RectTransform panel,
            RectTransform titleRect)
        {
            var haloObject = new GameObject(
                "BreakthroughHalo",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            haloObject.transform.SetParent(panel, false);
            haloObject.transform.SetAsFirstSibling();
            _breakthroughHalo = haloObject.GetComponent<RectTransform>();
            _breakthroughHalo.anchorMin = _breakthroughHalo.anchorMax =
                new Vector2(0.5f, 0.58f);
            _breakthroughHalo.sizeDelta = new Vector2(760f, 760f);
            Image halo = haloObject.GetComponent<Image>();
            halo.sprite = RuntimeArtLoader.GetVFXSprite("ink_dot");
            halo.color = new Color(0.95f, 0.67f, 0.22f, 0f);
            halo.preserveAspect = true;
            halo.raycastTarget = false;

            var sheenObject = new GameObject(
                "TitleSheen",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            sheenObject.transform.SetParent(titleRect, false);
            _titleSheen = sheenObject.GetComponent<RectTransform>();
            _titleSheen.anchorMin = new Vector2(0f, 0f);
            _titleSheen.anchorMax = new Vector2(0f, 1f);
            _titleSheen.pivot = new Vector2(0.5f, 0.5f);
            _titleSheen.sizeDelta = new Vector2(52f, -14f);
            _titleSheen.anchoredPosition = new Vector2(-52f, 0f);
            Image sheen = sheenObject.GetComponent<Image>();
            sheen.color = new Color(1f, 0.85f, 0.42f, 0f);
            sheen.raycastTarget = false;
        }

        private IEnumerator AnimateEnhancedEntrance()
        {
            float duration = 0.34f;
            float elapsed = 0f;
            var starts = new Vector2[_choiceSlots.Count];
            for (int i = 0; i < _choiceSlots.Count; i++)
            {
                RectTransform slot = _choiceSlots[i];
                if (slot == null) continue;
                starts[i] = slot.anchoredPosition;
                slot.anchoredPosition += Vector2.down * 42f;
                CanvasGroup group = slot.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 0f;
            }

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                if (_rootGroup != null)
                    _rootGroup.alpha = Mathf.Clamp01(t / 0.30f);
                if (_panel != null)
                {
                    float scale = t < 0.72f
                        ? Mathf.Lerp(0.90f, 1.022f, t / 0.72f)
                        : Mathf.Lerp(1.022f, 1f, (t - 0.72f) / 0.28f);
                    _panel.localScale = Vector3.one * scale;
                }
                if (_breakthroughHalo != null)
                {
                    float haloT = Mathf.Clamp01(t / 0.72f);
                    _breakthroughHalo.localScale = Vector3.one *
                        Mathf.Lerp(0.34f, 1.08f, haloT);
                    Image halo = _breakthroughHalo.GetComponent<Image>();
                    Color color = halo.color;
                    color.a = Mathf.Sin(haloT * Mathf.PI) * 0.15f;
                    halo.color = color;
                }
                if (_titleSheen != null)
                {
                    float sheenT = Mathf.Clamp01((t - 0.18f) / 0.55f);
                    RectTransform title =
                        _titleSheen.parent as RectTransform;
                    float width = title != null
                        ? title.rect.width
                        : 420f;
                    _titleSheen.anchoredPosition = new Vector2(
                        Mathf.Lerp(-52f, width + 52f, sheenT),
                        0f);
                    Image sheen = _titleSheen.GetComponent<Image>();
                    Color color = sheen.color;
                    color.a = Mathf.Sin(sheenT * Mathf.PI) * 0.38f;
                    sheen.color = color;
                }
                for (int i = 0; i < _choiceSlots.Count; i++)
                {
                    RectTransform slot = _choiceSlots[i];
                    if (slot == null) continue;
                    float local = Mathf.Clamp01(
                        (elapsed - i * 0.045f) / 0.22f);
                    float eased = 1f - Mathf.Pow(1f - local, 3f);
                    slot.anchoredPosition = Vector2.Lerp(
                        starts[i] + Vector2.down * 42f,
                        starts[i],
                        eased);
                    CanvasGroup group = slot.GetComponent<CanvasGroup>();
                    if (group != null) group.alpha = local;
                }
                yield return null;
            }

            if (_rootGroup != null) _rootGroup.alpha = 1f;
            if (_panel != null) _panel.localScale = Vector3.one;
            for (int i = 0; i < _choiceSlots.Count; i++)
            {
                RectTransform slot = _choiceSlots[i];
                if (slot == null) continue;
                slot.anchoredPosition = starts[i];
                CanvasGroup group = slot.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1f;
            }
            SetButtonsInteractable(true);
            _animation = null;
        }

        private void DestroyRootState()
        {
            unchecked { _viewGeneration++; }
            GameObject root = Root;
            Root = null;
            _cardTooltip?.Hide();
            _cardTooltip = null;
            _choiceButtons.Clear();
            _utilityButtons.Clear();
            _choiceSlots.Clear();
            _choiceRestPositions.Clear();
            ChoiceArea = null;
            _rootGroup = null;
            _panel = null;
            _breakthroughHalo = null;
            _titleSheen = null;
            _isClosing = false;
            _suspended = false;
            _closingSelectedIndex = -1;
            _closeCompletion = null;
            if (root != null)
            {
                root.SetActive(false);
                Destroy(root);
            }
        }

        private static RectTransform CreateRect(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static int CountActions(
            IReadOnlyList<RewardUtilityAction> actions)
        {
            if (actions == null) return 0;
            int count = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && actions[i].Execute != null)
                    count++;
            }
            return count;
        }

        private void OnDestroy()
        {
            Close();
        }

        private void OnDisable() => SuspendPresentation();

        private void OnEnable()
        {
            if (_suspended) ResumePresentation();
        }
    }
}
