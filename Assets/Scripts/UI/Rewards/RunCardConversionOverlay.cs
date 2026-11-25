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
    /// 归元碑的奖励窗式选择面板。它只拥有候选卡墙、当前选择、一次提交门禁
    /// 与提交后的表现交接；候选过滤和权威结算仍由 Dungeon/Bootstrap 拥有。
    /// 点击卡牌只更新结果预览，确认后才发出提交意图；成功后卡牌居中短暂停留，再解形飞向打开
    /// 面板时冻结的地图碑芯锚点，不在 Canvas 中复制一座归元碑。
    /// 规模审查：本文件超过 500 行，但候选卡墙、右栏预览与提交门禁共享同一
    /// Overlay 生命周期；粒子网格、业务规则和事务状态已由独立所有者封装。
    /// 修碑复用同一提交生命周期；标签和可用按钮来自不可变表现策略，
    /// 不在面板复制卡牌资格或事务规则。若加入第二组生命周期必须重新拆分。
    /// </summary>
    public sealed class RunCardConversionOverlay : MonoBehaviour
    {
        private enum OverlayState
        {
            Selection,
            Submitting,
            Committed
        }

        private static readonly Color Jade = RewardPanelFactory.JadeAccent;
        private static readonly Color JadeHighlight =
            RewardPanelFactory.JadeHighlight;
        private static readonly Color WarmGold = RewardPanelFactory.WarmGold;
        private static readonly Color ErrorColor =
            new Color(0.94f, 0.52f, 0.42f, 1f);

        private readonly List<RunCardConversionOptionViewData> _options =
            new List<RunCardConversionOptionViewData>();
        private readonly List<CardView> _cards = new List<CardView>();
        private readonly List<CanvasGroup> _cardGroups =
            new List<CanvasGroup>();
        private readonly List<RectTransform> _cardSlots =
            new List<RectTransform>();

        private CanvasGroup _rootGroup;
        private Image _scrimImage;
        private RectTransform _panel;
        private CanvasGroup _selectionGroup;
        private RectTransform _targetAnchor;
        private Text _selectedName;
        private Text _effectText;
        private Text _warningText;
        private Text _statusText;
        private Button _confirmButton;
        private Button _cashOutButton;
        private Button _backButton;
        private RunCardConversionOutcomeAnimator _outcomeAnimator;
        private Action<RunCardConversionOverlayIntent> _onConfirmed;
        private Action _onCancelled;
        private Action _committedCompletion;
        private Coroutine _transition;
        private OverlayState _state;
        private RunCardConversionOverlayIntent _pendingIntent;
        private int _selectedIndex = -1;
        private int _cashOutAmount;
        private Vector2 _mapTargetScreenPosition;
        private RunCardConversionPresentation _presentation =
            RunCardConversionPresentation.Guiyuan;

        public GameObject Root { get; private set; }
        public bool IsShown => Root != null && Root.activeInHierarchy;

        public void Show(
            Canvas canvas,
            IReadOnlyList<RunCardConversionOptionViewData> options,
            int cashOutAmount,
            Vector2 mapTargetScreenPosition,
            Action<RunCardConversionOverlayIntent> onConfirmed,
            Action onCancelled,
            RunCardConversionPresentation presentation = null)
        {
            CloseSilently();
            if (canvas == null)
                throw new ArgumentNullException(nameof(canvas));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            for (int index = 0; index < options.Count; index++)
            {
                if (options[index] != null)
                    _options.Add(options[index]);
            }

            _cashOutAmount = Mathf.Max(0, cashOutAmount);
            _presentation = presentation ?? RunCardConversionPresentation.Guiyuan;
            _mapTargetScreenPosition = mapTargetScreenPosition;
            _onConfirmed = onConfirmed;
            _onCancelled = onCancelled;
            _selectedIndex = -1;
            _state = OverlayState.Selection;

            Root = RewardPanelFactory.CreateRoot(
                canvas,
                "RunCardConversionOverlay",
                false,
                out _rootGroup,
                out _);
            _scrimImage = Root.GetComponent<Image>();
            _panel = RewardPanelFactory.CreatePanel(
                Root.transform,
                "RunCardConversionRewardPanel",
                new Vector2(1660f, 860f),
                out _);

            BuildHeader();
            RectTransform selection = CreateRect(
                _panel, "Selection", Vector2.zero, Vector2.one);
            _selectionGroup = selection.gameObject.AddComponent<CanvasGroup>();
            BuildCandidates(selection);
            BuildRightRail(selection);
            RefreshSelection();

            _outcomeAnimator = Root.AddComponent<
                RunCardConversionOutcomeAnimator>();
            SetSelectionInteractable(false);
            _transition = StartCoroutine(AnimateOpen());
        }

        public bool ShowCommitFailure(string error)
        {
            if (Root == null || _state != OverlayState.Submitting)
                return false;

            _state = OverlayState.Selection;
            _pendingIntent = default;
            string detail = error?.Trim();
            _statusText.text = string.IsNullOrWhiteSpace(detail)
                ? "结算未完成，请重新确认。"
                : detail.Length <= 18
                    ? "结算未完成 · " + detail
                    : "结算未完成，请重新选择后再试。";
            _statusText.color = ErrorColor;
            _statusText.gameObject.SetActive(true);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                Debug.LogWarning(
                    $"[RunCardConversionOverlay] 归元提交失败：{detail}");
            }
            SetSelectionInteractable(true);
            return true;
        }

        public bool PlayCommitted(int committedAmount, Action onClosed)
        {
            if (Root == null || _state != OverlayState.Submitting ||
                _outcomeAnimator == null)
                return false;

            bool convertsCard = _pendingIntent.Kind ==
                RunCardConversionOverlayIntentKind.ConvertCard;
            if (convertsCard && !TryGetSelected(out _))
                return false;

            _state = OverlayState.Committed;
            _committedCompletion = onClosed;
            SetSelectionInteractable(false);
            PrepareCommittedStage(
                convertsCard,
                committedAmount,
                out CanvasGroup cardGroup,
                out RectTransform cardRect);

            _outcomeAnimator.Prepare(
                Root.transform as RectTransform,
                cardGroup,
                cardRect,
                _targetAnchor,
                _presentation.IsManaConversion);
            if (_outcomeAnimator.Play(_pendingIntent.Kind, FinishCommitted))
                return true;

            FinishCommitted();
            return false;
        }

        private IEnumerator AnimateOpen()
        {
            yield return RewardPanelFactory.AnimateIn(
                _rootGroup, _panel, 0.24f);
            _transition = null;
            if (_state == OverlayState.Selection)
                SetSelectionInteractable(true);
        }

        private void BuildHeader()
        {
            RewardPanelFactory.CreateHeader(_panel, _presentation.Title);
        }

        private void BuildCandidates(RectTransform selection)
        {
            RectTransform viewport = CreateRect(
                selection,
                "CandidateViewport",
                RewardPanelFactory.CandidateAreaMin,
                RewardPanelFactory.CandidateAreaMax);
            Image maskImage = viewport.gameObject.AddComponent<Image>();
            maskImage.color = new Color(0f, 0f, 0f, 0.001f);
            maskImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 42f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform content = CreateRect(
                viewport,
                "CandidateContent",
                new Vector2(0f, 1f),
                Vector2.one);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            GridLayoutGroup layout = content.gameObject.AddComponent<
                GridLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 16, 24);
            layout.cellSize = new Vector2(276f, 382f);
            layout.spacing = new Vector2(18f, 24f);
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;

            ContentSizeFitter fitter = content.gameObject.AddComponent<
                ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;

            if (_options.Count == 0)
            {
                Text empty = RewardPanelFactory.CreateText(
                    viewport,
                    "EmptyCandidates",
                    _presentation.AllowsCashOut
                        ? $"当前没有可{_presentation.ActionLabel}的卡牌\n仍可在右侧将碑中余韵化为灵玉"
                        : "当前没有可用的永久卡牌\n可先离开，不消耗卡牌，也不获得属性",
                    28,
                    TextAnchor.MiddleCenter,
                    RewardPanelFactory.SecondaryText);
                Place(empty.rectTransform,
                    new Vector2(0.15f, 0.28f), new Vector2(0.85f, 0.72f));
                return;
            }

            for (int index = 0; index < _options.Count; index++)
            {
                int captured = index;
                GameObject slotObject = new GameObject(
                    $"Candidate_{index}", typeof(RectTransform));
                slotObject.transform.SetParent(content, false);
                RectTransform slot = slotObject.GetComponent<RectTransform>();
                _cardSlots.Add(slot);

                CanvasGroup group = slotObject.AddComponent<CanvasGroup>();
                _cardGroups.Add(group);

                CardView card = CardViewFactory.Create(
                    slot, addHoverHandler: false);
                RectTransform cardRect = card.transform as RectTransform;
                cardRect.anchorMin = cardRect.anchorMax =
                    new Vector2(0.5f, 0.5f);
                cardRect.anchoredPosition = Vector2.zero;
                cardRect.localScale = Vector3.one;
                card.UpdateCard(
                    CardDisplayData.FromCard(
                        _options[index].Card, canPlay: true),
                    index);
                card.cardButton.onClick.AddListener(
                    () => SelectCandidate(captured));
                _cards.Add(card);

                UI.Components.HoverScaleEffect hover =
                    slotObject.AddComponent<UI.Components.HoverScaleEffect>();
                hover.NormalScale = 1f;
                hover.HoverScale = 1.045f;
            }
        }

        private void BuildRightRail(RectTransform selection)
        {
            RectTransform rail = RewardPanelFactory.CreateResultRail(
                selection,
                "ResultRail",
                _presentation.ActionLabel + "所得");

            _selectedName = RewardPanelFactory.CreateText(
                rail,
                "SelectedName",
                string.Empty,
                27,
                TextAnchor.MiddleLeft,
                RewardPanelFactory.PrimaryText);
            Place(_selectedName.rectTransform,
                new Vector2(0.10f, 0.73f), new Vector2(0.90f, 0.83f));

            _effectText = RewardPanelFactory.CreateText(
                rail,
                "ConversionEffect",
                string.Empty,
                25,
                TextAnchor.UpperLeft,
                JadeHighlight);
            Place(_effectText.rectTransform,
                new Vector2(0.10f, 0.56f), new Vector2(0.90f, 0.72f));

            _warningText = RewardPanelFactory.CreateText(
                rail,
                "ConversionWarning",
                string.Empty,
                17,
                TextAnchor.UpperLeft,
                WarmGold);
            Place(_warningText.rectTransform,
                new Vector2(0.10f, 0.43f), new Vector2(0.90f, 0.55f));

            _statusText = RewardPanelFactory.CreateText(
                rail,
                "Status",
                string.Empty,
                17,
                TextAnchor.MiddleLeft,
                ErrorColor);
            Place(_statusText.rectTransform,
                new Vector2(0.10f, 0.32f), new Vector2(0.90f, 0.43f));
            _statusText.verticalOverflow = VerticalWrapMode.Overflow;
            _statusText.gameObject.SetActive(false);

            RewardPanelFactory.CreateRailActionDivider(rail, 0.30f);

            _confirmButton = RewardPanelFactory.CreateButton(
                rail,
                "ConfirmConversion",
                "确认" + _presentation.ActionLabel,
                RewardPanelFactory.PrimaryAction);
            PlaceAt(_confirmButton.transform as RectTransform,
                new Vector2(0.5f, 0.245f), new Vector2(232f, 56f));
            _confirmButton.onClick.AddListener(SubmitSelectedCard);

            _cashOutButton = RewardPanelFactory.CreateButton(
                rail,
                "CashOut",
                $"化为灵玉 · +{_cashOutAmount}",
                RewardPanelFactory.WarmAction);
            PlaceAt(_cashOutButton.transform as RectTransform,
                new Vector2(0.5f, 0.15f), new Vector2(232f, 56f));
            _cashOutButton.onClick.AddListener(SubmitCashOut);
            _cashOutButton.gameObject.SetActive(_presentation.AllowsCashOut);

            _backButton = RewardPanelFactory.CreateButton(
                rail,
                "BackButton",
                "离开",
                RewardPanelFactory.NeutralAction);
            PlaceAt(_backButton.transform as RectTransform,
                new Vector2(0.5f, 0.055f), new Vector2(232f, 56f));
            _backButton.onClick.AddListener(Cancel);
            _backButton.gameObject.SetActive(_presentation.AllowsLeave);
        }

        private void SelectCandidate(int index)
        {
            if (_state != OverlayState.Selection || index < 0 ||
                index >= _options.Count)
                return;

            _selectedIndex = index;
            _statusText.gameObject.SetActive(false);
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            bool anySelection = _selectedIndex >= 0;
            bool hasCandidates = _options.Count > 0;
            _confirmButton.gameObject.SetActive(hasCandidates);
            PlaceAt(
                _cashOutButton.transform as RectTransform,
                new Vector2(0.5f, hasCandidates ? 0.15f : 0.20f),
                new Vector2(232f, 56f));
            PlaceAt(
                _backButton.transform as RectTransform,
                new Vector2(0.5f, _presentation.AllowsCashOut
                    ? (hasCandidates ? 0.055f : 0.105f)
                    : 0.15f),
                new Vector2(232f, 56f));
            for (int index = 0; index < _cards.Count; index++)
            {
                bool selected = index == _selectedIndex;
                _cards[index].SetSelected(selected);
                _cardGroups[index].alpha = selected || !anySelection
                    ? 1f
                    : 0.78f;
            }

            bool hasSelection = TryGetSelected(
                out RunCardConversionOptionViewData option);
            if (!hasSelection)
            {
                _selectedName.text = hasCandidates
                    ? "选择一张卡牌"
                    : _presentation.AllowsCashOut ? "碑中余韵" : "暂无可用卡牌";
                _effectText.text = hasCandidates
                    ? $"选择后查看移除代价与{_presentation.ActionLabel}所得"
                    : _presentation.AllowsCashOut
                        ? $"化为灵玉 · +{_cashOutAmount}"
                        : "离开不会改变本次探索的卡牌与属性";
                _effectText.color = hasCandidates
                    ? JadeHighlight
                    : WarmGold;
                _warningText.text = string.Empty;
                _confirmButton.interactable = false;
                return;
            }

            _selectedName.text = option.Card.cardName;
            _effectText.text = option.LossText + "\n" + option.GainText;
            _effectText.color = JadeHighlight;
            _warningText.text = option.WarningText;
            _confirmButton.interactable =
                _state == OverlayState.Selection;
        }

        private void SubmitSelectedCard()
        {
            if (_state != OverlayState.Selection ||
                !TryGetSelected(out RunCardConversionOptionViewData option))
                return;

            _pendingIntent = RunCardConversionOverlayIntent.ConvertCard(
                option.RuntimeInstanceId);
            SubmitPendingIntent($"正在{_presentation.ActionLabel}，等待本次探索完成结算……");
        }

        private void SubmitCashOut()
        {
            if (_state != OverlayState.Selection || !_presentation.AllowsCashOut)
                return;

            _pendingIntent = RunCardConversionOverlayIntent.CashOut();
            SubmitPendingIntent("正在将碑中余韵化为灵玉……");
        }

        private void Cancel()
        {
            if (_state != OverlayState.Selection || !_presentation.AllowsLeave)
                return;
            Action cancelled = _onCancelled;
            _onCancelled = null;
            CloseSilently();
            cancelled?.Invoke();
        }

        private void SubmitPendingIntent(string status)
        {
            _state = OverlayState.Submitting;
            SetSelectionInteractable(false);
            _statusText.text = status;
            _statusText.color = JadeHighlight;
            _statusText.gameObject.SetActive(true);
            _onConfirmed?.Invoke(_pendingIntent);
        }

        private void PrepareCommittedStage(
            bool convertsCard,
            int committedAmount,
            out CanvasGroup cardGroup,
            out RectTransform cardRect)
        {
            if (_transition != null)
                StopCoroutine(_transition);
            _transition = null;
            Canvas.ForceUpdateCanvases();

            cardGroup = null;
            cardRect = null;
            if (convertsCard && _selectedIndex >= 0 &&
                _selectedIndex < _cardSlots.Count)
            {
                RectTransform slot = _cardSlots[_selectedIndex];
                slot.SetParent(Root.transform, false);
                PlaceAt(
                    slot,
                    new Vector2(0.50f, 0.56f),
                    new Vector2(276f, 382f));
                slot.SetAsLastSibling();
                UI.Components.HoverScaleEffect hover =
                    slot.GetComponent<UI.Components.HoverScaleEffect>();
                if (hover != null)
                    hover.enabled = false;
                slot.localScale = Vector3.one;
                cardGroup = _cardGroups[_selectedIndex];
                cardGroup.alpha = 1f;
                cardGroup.interactable = false;
                cardGroup.blocksRaycasts = false;
                _cards[_selectedIndex].SetSelected(false);
                _cards[_selectedIndex].cardButton.interactable = false;
                cardRect = _cards[_selectedIndex].transform as RectTransform;
            }

            if (!convertsCard)
            {
                _effectText.text =
                    $"化为灵玉 · +{Mathf.Max(0, committedAmount)}";
                _effectText.color = WarmGold;
            }
            else if (TryGetSelected(out RunCardConversionOptionViewData selected))
            {
                _effectText.text = selected.GainText;
            }
            _effectText.transform.SetParent(Root.transform, false);
            PlaceAt(_effectText.rectTransform,
                new Vector2(0.5f, 0.26f), new Vector2(680f, 80f));
            _effectText.fontSize = 28;
            _effectText.transform.SetAsLastSibling();

            _targetAnchor = CreateRect(
                Root.transform,
                "CapturedMapSteleAnchor",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f));
            _targetAnchor.sizeDelta = new Vector2(8f, 8f);
            RectTransform rootRect = Root.transform as RectTransform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rootRect,
                    _mapTargetScreenPosition,
                    null,
                    out Vector2 targetLocalPosition))
            {
                _targetAnchor.anchoredPosition = targetLocalPosition;
            }
            _panel.gameObject.SetActive(false);
            _scrimImage.color = Color.clear;
            _rootGroup.alpha = 1f;
            _rootGroup.interactable = false;
            _rootGroup.blocksRaycasts = true;
        }

        private void FinishCommitted()
        {
            if (_state != OverlayState.Committed)
                return;
            CloseSilently();
        }

        private bool TryGetSelected(
            out RunCardConversionOptionViewData option)
        {
            if (_selectedIndex >= 0 && _selectedIndex < _options.Count)
            {
                option = _options[_selectedIndex];
                return option != null;
            }

            option = null;
            return false;
        }

        private void SetSelectionInteractable(bool value)
        {
            if (_selectionGroup != null)
            {
                _selectionGroup.interactable = value;
                _selectionGroup.blocksRaycasts = value;
            }
            for (int index = 0; index < _cards.Count; index++)
                _cards[index].cardButton.interactable = value;
            if (_confirmButton != null)
            {
                _confirmButton.interactable = value &&
                    _selectedIndex >= 0;
            }
            if (_cashOutButton != null)
                _cashOutButton.interactable = value && _presentation.AllowsCashOut;
            if (_backButton != null)
                _backButton.interactable = value && _presentation.AllowsLeave;
        }

        public void CloseSilently()
        {
            if (_transition != null)
                StopCoroutine(_transition);
            _transition = null;
            _outcomeAnimator?.StopAndClear();
            if (_rootGroup != null)
            {
                _rootGroup.interactable = false;
                _rootGroup.blocksRaycasts = false;
            }

            Action committedCompletion = _committedCompletion;
            _committedCompletion = null;
            _options.Clear();
            _cards.Clear();
            _cardGroups.Clear();
            _cardSlots.Clear();
            _onConfirmed = null;
            _onCancelled = null;
            _rootGroup = null;
            _scrimImage = null;
            _panel = null;
            _selectionGroup = null;
            _targetAnchor = null;
            _selectedName = null;
            _effectText = null;
            _warningText = null;
            _statusText = null;
            _confirmButton = null;
            _cashOutButton = null;
            _backButton = null;
            _outcomeAnimator = null;
            _selectedIndex = -1;
            _state = OverlayState.Selection;
            _pendingIntent = default;
            _presentation = RunCardConversionPresentation.Guiyuan;

            if (Root != null)
                Destroy(Root);
            Root = null;
            committedCompletion?.Invoke();
        }

        private static RectTransform CreateRect(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            Place(rect, anchorMin, anchorMax);
            return rect;
        }

        private static void Place(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void PlaceAt(
            RectTransform rect,
            Vector2 anchor,
            Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        private void OnDisable()
        {
            CloseSilently();
        }
    }
}
