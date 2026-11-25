using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// Owns read-only pile browsing, input lease, scrolling and card tooltips.
    /// A dedicated status owner renders the draw pile's current pending Confuse
    /// count; it never owns a second pile, insertion rule or affected-card state.
    /// Combat remains the sole owner of pile contents and order.
    /// </summary>
    public class DeckPileView : UIView
    {
        public Text drawPileCount;
        public Text discardPileCount;
        public Button drawPileToggle;
        public Button discardPileToggle;
        private CombatController _combat;
        private PlayPileView _playPile;
        private HandView _handView;
        private HandViewModel _handVM;
        private Func<IDisposable> _acquireInputLock;
        private Func<bool> _canOpen;
        private IDisposable _inputLease;
        private RectTransform _overlay;
        private RectTransform _content;
        private Text _title;
        private Text _empty;
        private ScrollRect _scroll;
        private GridLayoutGroup _grid;
        private CardTooltipView _tooltip;
        private CardPresentationProfile _presentation;
        private readonly List<CardView> _cards = new List<CardView>();
        private readonly List<CardConfig> _snapshot = new List<CardConfig>();
        private bool _showDiscard;
        private bool _isOpen;
        private DeckConfusionStatusPresentation _confusionStatus;

        public void Configure(CombatController combat, Text drawCount, Text discardCount,
            PlayPileView playPile, Func<IDisposable> acquireInputLock, Func<bool> canOpen,
            HandView handView = null)
        {
            if (_combat != null) _combat.OnStateChanged -= OnCombatChanged;
            _combat = combat;
            _playPile = playPile;
            _handView = handView;
            _acquireInputLock = acquireInputLock;
            _canOpen = canOpen;
            drawPileCount = drawCount;
            discardPileCount = discardCount;
            drawPileToggle = CreatePileButton(drawCount, "InspectDrawPile", () => Open(false));
            discardPileToggle = CreatePileButton(discardCount, "InspectDiscardPile", () => Open(true));
            playPile?.ConfigureInspection(() => Open(true));
            if (_combat != null) _combat.OnStateChanged += OnCombatChanged;
            RefreshConfusionStatus();
        }

        public override void Bind(ViewModelBase viewModel)
        {
            base.Bind(viewModel);
            _handVM = viewModel as HandViewModel;
            RefreshCounts();
        }

        public override void Unbind()
        {
            Close();
            _confusionStatus?.Hide();
            _handVM = null;
            base.Unbind();
        }

        protected override void OnViewModelChanged(string propertyName)
        {
            if (propertyName == "*" || propertyName == HandViewModel.PROP_PILE_COUNTS)
                RefreshCounts();
        }

        private void RefreshCounts()
        {
            RefreshConfusionStatus();
            if (_handVM == null) return;
            if (drawPileCount != null) drawPileCount.text = _handVM.DeckCount.ToString();
            if (discardPileCount != null) discardPileCount.text = _handVM.DiscardCount.ToString();
        }

        private void RefreshConfusionStatus()
        {
            if (!isActiveAndEnabled || _handVM == null || _combat == null || drawPileCount == null)
            {
                _confusionStatus?.Hide();
                return;
            }

            int remaining = 0;
            // Membership in the live draw pile is the only truth. A consumed
            // Confuse card, a discard card or an affected hand card is not pending.
            IReadOnlyList<CardConfig> drawPile = _combat.State.DrawPile;
            for (int i = 0; i < drawPile.Count; i++)
                if (drawPile[i] != null &&
                    drawPile[i].contentId == "card.xiyou.confuse") remaining++;
            if (remaining <= 0)
            {
                _confusionStatus?.Hide();
                return;
            }

            if (_confusionStatus == null || !_confusionStatus.IsAnchoredTo(drawPileCount))
            {
                _confusionStatus?.Dispose();
                _confusionStatus = DeckConfusionStatusPresentation.Create(drawPileCount);
            }
            _confusionStatus.Present(remaining);
        }

        public void ShowDrawPile() => Open(false);
        public void ShowDiscardPile() => Open(true);

        /// <summary>
        /// 从当前真实实例所在牌区取得表现终点；不移动卡牌，不改变牌区顺序。
        /// 出牌库使用可见牌堆，抽牌库使用计数入口，手牌回到该实例自身的位置。
        /// </summary>
        public bool TryGetCardReturnTarget(int runtimeInstanceId, out SealCardReturnTarget target)
        {
            target = default;
            if (_combat == null || runtimeInstanceId <= 0) return false;
            RectTransform destination;
            float cardHeight;
            if (ContainsInstance(_combat.Hand, runtimeInstanceId))
            {
                CardView card = _handView != null ? _handView.FindInstanceView(runtimeInstanceId) : null;
                destination = card != null ? (card.visualRoot != null
                    ? card.visualRoot : card.transform as RectTransform) : null;
                cardHeight = destination != null ? destination.rect.height : 0f;
            }
            else if (ContainsInstance(_combat.Deck, runtimeInstanceId))
            {
                destination = drawPileCount != null ? drawPileCount.rectTransform : null;
                cardHeight = 80f;
            }
            else if (ContainsInstance(_combat.Discard, runtimeInstanceId))
            {
                destination = _playPile != null ? _playPile.transform as RectTransform :
                    (discardPileCount != null ? discardPileCount.rectTransform : null);
                cardHeight = _playPile != null
                    ? CardPresentationProfile.LoadDefault().referenceSize.y * _playPile.PileCardScale
                    : 80f;
            }
            else return false;
            if (destination == null || !destination.gameObject.activeInHierarchy) return false;
            Canvas canvas = destination.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            Vector3 centre = destination.TransformPoint(destination.rect.center);
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, centre);
            float screenHeight = Vector2.Distance(screenPoint,
                RectTransformUtility.WorldToScreenPoint(camera,
                    destination.TransformPoint(destination.rect.center + Vector2.up * cardHeight)));
            target = new SealCardReturnTarget(screenPoint, screenHeight, destination.eulerAngles.z);
            return true;
        }

        private static bool ContainsInstance(IReadOnlyList<CardConfig> cards, int runtimeInstanceId)
        {
            for (int i = 0; i < cards.Count; i++)
                if (cards[i] != null && cards[i].runtimeInstanceId == runtimeInstanceId) return true;
            return false;
        }

        private void Open(bool discard)
        {
            if (_combat == null || _handVM == null || (!_isOpen && _canOpen != null && !_canOpen())) return;
            EnsurePanel();
            _showDiscard = discard;
            if (!_isOpen) _inputLease = _acquireInputLock?.Invoke();
            _isOpen = true;
            _overlay.gameObject.SetActive(true);
            transform.SetAsLastSibling();
            RefreshCards();
            _scroll.verticalNormalizedPosition = 1f;
        }

        private void Close()
        {
            _isOpen = false;
            _tooltip?.Hide();
            if (_overlay != null) _overlay.gameObject.SetActive(false);
            _inputLease?.Dispose();
            _inputLease = null;
        }

        private void OnEnable() => RefreshCounts();

        private void OnDisable()
        {
            Close();
            _confusionStatus?.Hide();
        }

        protected override void OnDestroy()
        {
            if (_combat != null) _combat.OnStateChanged -= OnCombatChanged;
            _confusionStatus?.Dispose();
            _confusionStatus = null;
            base.OnDestroy();
        }

        private void OnCombatChanged(CombatStateChangeReason reason)
        {
            RefreshConfusionStatus();
            if (_isOpen) RefreshCards();
        }

        private void RefreshCards()
        {
            _tooltip.Hide();
            _snapshot.Clear();
            IReadOnlyList<CardConfig> source = _showDiscard ? _combat.Discard : _combat.Deck;
            // Sort only this display snapshot, never the live pile or its draw order.
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null) _snapshot.Add(source[i]);
            _snapshot.Sort((left, right) => string.CompareOrdinal(left.contentId, right.contentId));
            _title.text = (_showDiscard ? "出牌库" : "抽牌库") + "  ·  " + _snapshot.Count + " 张";
            _empty.gameObject.SetActive(_snapshot.Count == 0);
            Canvas.ForceUpdateCanvases();
            _grid.constraintCount = Mathf.Max(1,
                Mathf.FloorToInt((_scroll.viewport.rect.width - 32f) / 200f));
            for (int i = 0; i < _snapshot.Count; i++)
            {
                if (i == _cards.Count) CreateCard(i);
                CardView view = _cards[i];
                view.transform.parent.gameObject.SetActive(true);
                // Browsing a pile describes the stored card's base context,
                // rather than claiming this non-hand card can be played now.
                view.UpdateCard(CardDisplayData.FromCard(_snapshot[i]), i);
            }
            for (int i = _snapshot.Count; i < _cards.Count; i++)
                _cards[i].transform.parent.gameObject.SetActive(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        private void CreateCard(int index)
        {
            // Same card and tooltip components as the hand, without play/drag behavior.
            RectTransform slot = Rect("CardSlot", _content);
            CardView view = CardViewFactory.Create(slot, _presentation, addHoverHandler: false);
            view.transform.localScale = Vector3.one * (180f / _presentation.referenceSize.x);
            _cards.Add(view);
            EventTrigger trigger = view.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowTooltip(index));
            trigger.triggers.Add(enter);
            var leave = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            leave.callback.AddListener(_ => _tooltip.Hide());
            trigger.triggers.Add(leave);
            ForwardScrollEvent(trigger, EventTriggerType.Scroll, _scroll.OnScroll);
            ForwardScrollEvent(trigger, EventTriggerType.InitializePotentialDrag, _scroll.OnInitializePotentialDrag);
            ForwardScrollEvent(trigger, EventTriggerType.BeginDrag, _scroll.OnBeginDrag);
            ForwardScrollEvent(trigger, EventTriggerType.Drag, _scroll.OnDrag);
            ForwardScrollEvent(trigger, EventTriggerType.EndDrag, _scroll.OnEndDrag);
            view.cardButton.onClick.AddListener(() => ShowTooltip(index));
        }

        private static void ForwardScrollEvent(EventTrigger trigger, EventTriggerType type,
            Action<PointerEventData> callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => callback((PointerEventData)data));
            trigger.triggers.Add(entry);
        }

        private void ShowTooltip(int index)
        {
            if (_isOpen && index >= 0 && index < _snapshot.Count)
                _tooltip.Show(_snapshot[index], null);
        }

        private void EnsurePanel()
        {
            if (_overlay != null) return;
            _presentation = CardPresentationProfile.LoadDefault();
            _overlay = Rect("PileBrowser", transform);
            Stretch(_overlay);
            _overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
            _overlay.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            RectTransform panel = Rect("Panel", _overlay);
            panel.anchorMin = new Vector2(0.09f, 0.10f);
            panel.anchorMax = new Vector2(0.91f, 0.90f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.10f, 0.08f, 0.98f);
            // Consume blank-panel clicks so only the outside shade dismisses it.
            panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            _title = Label("Title", panel, "", 28);
            _title.rectTransform.anchorMin = new Vector2(0f, 1f);
            _title.rectTransform.anchorMax = Vector2.one;
            _title.rectTransform.pivot = new Vector2(0.5f, 1f);
            _title.rectTransform.sizeDelta = new Vector2(-160f, 68f);
            Button close = TextButton(panel, "Close", "关闭", Close);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = Vector2.one;
            closeRect.anchoredPosition = new Vector2(-60f, -34f);
            closeRect.sizeDelta = new Vector2(96f, 44f);
            RectTransform viewport = Rect("Cards", panel);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(20f, 24f);
            viewport.offsetMax = new Vector2(-20f, -76f);
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;
            _content = Rect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = Vector2.zero;
            _scroll.content = _content;
            _grid = _content.gameObject.AddComponent<GridLayoutGroup>();
            float aspect = _presentation.referenceSize.y / _presentation.referenceSize.x;
            _grid.cellSize = new Vector2(180f, 180f * aspect);
            _grid.spacing = new Vector2(20f, 24f);
            _grid.padding = new RectOffset(16, 16, 12, 12);
            _grid.childAlignment = TextAnchor.UpperCenter;
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _empty = Label("Empty", panel, "这里暂时没有卡牌", 24);
            Stretch(_empty.rectTransform);
            var tooltipObject = new GameObject("PileCardTooltip", typeof(RectTransform));
            tooltipObject.transform.SetParent(_overlay, false);
            _tooltip = tooltipObject.AddComponent<CardTooltipView>();
        }

        private static Button CreatePileButton(Text count, string name, UnityEngine.Events.UnityAction action)
        {
            if (count == null) return null;
            RectTransform hit = Rect(name, count.transform);
            // The count and the adjacent arrow are separate browse targets; do not
            // extend this invisible target into neighbouring combat controls.
            hit.anchorMin = Vector2.zero;
            hit.anchorMax = Vector2.one;
            hit.offsetMin = hit.offsetMax = Vector2.zero;
            hit.gameObject.AddComponent<Image>().color = Color.clear;
            Button button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(action);
            return button;
        }

        private static Button TextButton(Transform parent, string name, string caption,
            UnityEngine.Events.UnityAction action)
        {
            RectTransform rect = Rect(name, parent);
            rect.gameObject.AddComponent<Image>().color = new Color(0.30f, 0.25f, 0.17f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(action);
            Stretch(Label("Label", rect, caption, 20).rectTransform);
            return button;
        }

        private static Text Label(string name, Transform parent, string text, int size)
        {
            Text label = Rect(name, parent).gameObject.AddComponent<Text>();
            label.font = Resources.Load<Font>("Fonts/NotoSerifSC-Medium")
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text;
            label.fontSize = size;
            label.color = new Color(0.94f, 0.88f, 0.73f);
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
