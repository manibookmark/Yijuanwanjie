using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 单个已确定奖励的统一反馈。固定使用同一标题与中央主体槽；
    /// 名称和规则均收入悬停 tooltip。默认在下一次点击时关闭，少数需要
    /// 改变奖励去向的流程可在原面板右侧附加工具动作，不另造奖励 UI。
    ///
    /// 规模审查：本文件超过 800 行，但奖励主体、tooltip、动作门禁和
    /// 关闭回调共同组成“单奖励展示”的一个状态机，强拆会泄漏 Root、
    /// DismissButton 与同一批协程状态，因此本次保留。成果卡 Hover 已交由
    /// ResultCardTooltipPresenter 独立拥有，只在主体入场结束时开放。
    /// 若未来加入候选生成、
    /// 业务提交、多步验证或第二组独立动作生命周期，必须重新审查并
    /// 拆出真实的状态所有者。
    /// </summary>
    public sealed class RewardRevealOverlay : MonoBehaviour
    {
        public GameObject Root { get; private set; }
        public Button DismissButton { get; private set; }
        public RectTransform HeaderArea { get; private set; }
        public RectTransform VisualArea { get; private set; }
        public Text TitleText { get; private set; }
        public RectTransform TooltipRoot { get; private set; }
        public TMP_Text TooltipTitleText { get; private set; }
        public TMP_Text TooltipBodyText { get; private set; }
        public bool IsShown => Root != null;
        public RewardMapVfxPreview MapVfxPreview { get; private set; }
        public IReadOnlyList<Button> UtilityButtons => _utilityButtons;

        public event Action<bool> VisualHoverChanged;
        public event Action Closed;

        private Action _onClosed;
        private Action _onPrimarySelected;
        private Coroutine _animation;
        private Coroutine _visualAnimation;
        private Coroutine _armDismiss;
        private Coroutine _tooltipAnimation;
        private CanvasGroup _tooltipGroup;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _panel;
        private bool _tooltipHovered;
        private RawImage _effectTexture;
        private RawImage _subjectEffectTexture;
        private Material _premultipliedTextureMaterial;
        private bool _reportedMissingPremultipliedShader;
        private readonly List<Button> _utilityButtons =
            new List<Button>();
        private bool _canDismiss;
        private bool _decisionActionsArmed;
        private ResultCardTooltipPresenter _resultCardTooltip;
        private Text _primarySelectionHint;

        public void Show(
            Canvas canvas,
            RewardFeedbackItem item,
            Action onClosed)
        {
            ShowInternal(
                canvas,
                item,
                onClosed,
                onPrimarySelected: null,
                canDismiss: true,
                actions: null);
        }

        /// <summary>
        /// 使用与普通获得反馈完全相同的主体模板，仅在右侧附加
        /// 已解锁的工具动作。onPrimarySelected 不为 null 时只有奖励主体
        /// 是主选择热区，不渲染独立按钮；为 null 时主体不响应点击。
        /// </summary>
        public void ShowDecision(
            Canvas canvas,
            RewardFeedbackItem item,
            Action onPrimarySelected,
            IReadOnlyList<RewardUtilityAction> actions)
        {
            if (onPrimarySelected == null &&
                !HasExecutableAction(actions))
            {
                throw new ArgumentException(
                    "Reward decision requires a primary selection or " +
                    "at least one executable utility action.",
                    nameof(actions));
            }
            ShowInternal(
                canvas,
                item,
                onClosed: null,
                onPrimarySelected: onPrimarySelected,
                canDismiss: false,
                actions: actions);
        }

        private void ShowInternal(
            Canvas canvas,
            RewardFeedbackItem item,
            Action onClosed,
            Action onPrimarySelected,
            bool canDismiss,
            IReadOnlyList<RewardUtilityAction> actions)
        {
            Close(invokeCallback: false);
            if (canvas == null)
                throw new ArgumentNullException(nameof(canvas));
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            _onClosed = onClosed;
            _onPrimarySelected = onPrimarySelected;
            _canDismiss = canDismiss;
            _decisionActionsArmed = false;
            _canvas = canvas;
            _canvasRect = canvas.transform as RectTransform;
            Root = RewardPanelFactory.CreateRoot(
                canvas,
                "RewardRevealOverlay",
                addDismissButton: true,
                out CanvasGroup rootGroup,
                out Button dismissButton);
            DismissButton = dismissButton;
            DismissButton.onClick.AddListener(RequestDismiss);
            DismissButton.interactable = false;

            _panel = RewardPanelFactory.CreatePanel(
                Root.transform,
                "RewardRevealPanel",
                new Vector2(760f, 720f),
                out _);
            BuildContent(_panel, item);
            BuildUtilityActions(_panel, actions);

            _animation = StartCoroutine(
                RewardPanelFactory.AnimateIn(rootGroup, _panel, 0.16f));
            _visualAnimation = StartCoroutine(AnimateSubjectIn());
            _armDismiss = StartCoroutine(ArmDismissNextFrame());
        }

        /// <summary>设置只承载入场粒子的透明纹理；不会替换奖励本体。</summary>
        public void SetEffectTexture(Texture texture)
        {
            if (Root == null) return;
            if (_effectTexture == null && texture == null) return;
            if (_effectTexture == null)
            {
                var go = new GameObject(
                    "RewardCelebrationEffectTexture",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage));
                // The panel Image is rendered before its children. Keeping the
                // effect as the first child places it above the opaque paper but
                // below every reward-content child created by BuildContent.
                go.transform.SetParent(
                    _panel != null ? _panel : Root.transform,
                    false);
                go.transform.SetAsFirstSibling();
                _effectTexture = go.GetComponent<RawImage>();
                RectTransform effectRect = _effectTexture.rectTransform;
                effectRect.anchorMin = effectRect.anchorMax =
                    new Vector2(0.5f, 0.5f);
                effectRect.pivot = new Vector2(0.5f, 0.5f);
                effectRect.sizeDelta = new Vector2(1440f, 1080f);
                effectRect.anchoredPosition = Vector2.zero;
                _effectTexture.raycastTarget = false;
                _effectTexture.material = GetPremultipliedTextureMaterial();
            }
            _effectTexture.texture = texture;
            _effectTexture.color = texture != null ? Color.white : Color.clear;
        }

        /// <summary>
        /// Foreground render target used by relics for the treasure's own
        /// ascension light. It sits inside the subject area, above the panel
        /// paper. The celebration target also sits above the paper, but below
        /// all normal reward content.
        /// </summary>
        public void SetSubjectEffectTexture(Texture texture)
        {
            if (VisualArea == null) return;
            if (_subjectEffectTexture == null && texture == null) return;
            if (_subjectEffectTexture == null)
            {
                var go = new GameObject(
                    "RewardSubjectEffectTexture",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage));
                go.transform.SetParent(VisualArea, false);
                _subjectEffectTexture = go.GetComponent<RawImage>();
                RewardPanelFactory.Stretch(
                    _subjectEffectTexture.rectTransform, -34f);
                _subjectEffectTexture.raycastTarget = false;
                _subjectEffectTexture.material =
                    GetPremultipliedTextureMaterial();
            }
            _subjectEffectTexture.texture = texture;
            _subjectEffectTexture.color = texture != null
                ? Color.white
                : Color.clear;
            _subjectEffectTexture.transform.SetAsFirstSibling();
        }

        public void Close()
        {
            Close(invokeCallback: true);
        }

        public void CloseSilently()
        {
            Close(invokeCallback: false);
        }

        private void RequestDismiss()
        {
            if (DismissButton == null || !DismissButton.interactable)
                return;
            if (!_canDismiss)
                return;

            Close(invokeCallback: true);
        }

        private void Close(bool invokeCallback)
        {
            StopTrackedCoroutines();
            _resultCardTooltip?.Hide();
            _resultCardTooltip = null;

            bool wasShown = Root != null;
            if (Root != null)
            {
                Destroy(Root);
                Root = null;
            }
            DismissButton = null;
            HeaderArea = null;
            VisualArea = null;
            TitleText = null;
            _primarySelectionHint = null;
            TooltipRoot = null;
            TooltipTitleText = null;
            TooltipBodyText = null;
            MapVfxPreview = null;
            _utilityButtons.Clear();
            _tooltipGroup = null;
            _canvas = null;
            _canvasRect = null;
            _panel = null;
            _tooltipHovered = false;
            _effectTexture = null;
            _subjectEffectTexture = null;
            _canDismiss = false;
            _decisionActionsArmed = false;

            Action callback = _onClosed;
            _onClosed = null;
            _onPrimarySelected = null;
            if (wasShown)
                Closed?.Invoke();
            if (invokeCallback && wasShown)
                callback?.Invoke();
        }

        private void StopTrackedCoroutines()
        {
            if (_animation != null)
            {
                StopCoroutine(_animation);
                _animation = null;
            }
            if (_visualAnimation != null)
            {
                StopCoroutine(_visualAnimation);
                _visualAnimation = null;
            }
            if (_armDismiss != null)
            {
                StopCoroutine(_armDismiss);
                _armDismiss = null;
            }
            if (_tooltipAnimation != null)
            {
                StopCoroutine(_tooltipAnimation);
                _tooltipAnimation = null;
            }
        }

        private IEnumerator ArmDismissNextFrame()
        {
            yield return null;
            _decisionActionsArmed = true;
            if (DismissButton != null)
                DismissButton.interactable = _canDismiss;
            for (int i = 0; i < _utilityButtons.Count; i++)
            {
                if (_utilityButtons[i] != null)
                    _utilityButtons[i].interactable = true;
            }
            RefreshPrimarySelectionHint();
            _armDismiss = null;
        }

        private void BuildUtilityActions(
            RectTransform panel,
            IReadOnlyList<RewardUtilityAction> actions)
        {
            int validCount = 0;
            for (int i = 0; actions != null && i < actions.Count; i++)
            {
                if (actions[i] != null && actions[i].Execute != null)
                    validCount++;
            }
            if (validCount == 0) return;

            var railObject = new GameObject(
                "UtilityActionRail",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup));
            railObject.transform.SetParent(panel, false);
            RectTransform rail = railObject.GetComponent<RectTransform>();
            rail.anchorMin = rail.anchorMax = new Vector2(1f, 0.5f);
            rail.pivot = new Vector2(0f, 0.5f);
            rail.anchoredPosition = new Vector2(28f, -18f);
            rail.sizeDelta = new Vector2(
                220f,
                validCount * 64f + Math.Max(0, validCount - 1) * 16f);

            VerticalLayoutGroup layout =
                railObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.padding = new RectOffset(8, 8, 0, 0);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            for (int i = 0; actions != null && i < actions.Count; i++)
            {
                RewardUtilityAction action = actions[i];
                if (action == null || action.Execute == null) continue;

                Button button = RewardPanelFactory.CreateButton(
                    rail,
                    "Utility_" + action.Kind,
                    action.Label,
                    RewardPanelFactory.ResolveUtilityAccent(action.Kind));
                (button.transform as RectTransform).sizeDelta =
                    new Vector2(204f, 64f);
                button.interactable = false;
                RewardUtilityAction captured = action;
                button.onClick.AddListener(
                    () => ExecuteUtilityAction(captured));
                _utilityButtons.Add(button);
            }
        }

        private void RequestSubjectSelection()
        {
            if (!_decisionActionsArmed || _onPrimarySelected == null)
                return;

            Action primary = _onPrimarySelected;
            SetDecisionButtonsInteractable(false);
            Close(invokeCallback: false);
            primary.Invoke();
        }

        private static bool HasExecutableAction(
            IReadOnlyList<RewardUtilityAction> actions)
        {
            if (actions == null) return false;
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && actions[i].Execute != null)
                    return true;
            }
            return false;
        }

        private void ExecuteUtilityAction(RewardUtilityAction action)
        {
            if (!_decisionActionsArmed || action == null ||
                action.Execute == null)
                return;
            SetDecisionButtonsInteractable(false);
            action.Execute();
        }

        private void SetDecisionButtonsInteractable(bool value)
        {
            _decisionActionsArmed = value;
            for (int i = 0; i < _utilityButtons.Count; i++)
            {
                if (_utilityButtons[i] != null)
                    _utilityButtons[i].interactable = value;
            }
            if (DismissButton != null)
                DismissButton.interactable = value && _canDismiss;
            RefreshPrimarySelectionHint();
        }

        private void RequestVisualSelection()
        {
            if (!_canDismiss && _onPrimarySelected != null)
            {
                RequestSubjectSelection();
                return;
            }
            RequestDismiss();
        }

        private void BuildContent(
            RectTransform panel,
            RewardFeedbackItem item)
        {
            var headerGo = new GameObject(
                "RewardHeaderArea",
                typeof(RectTransform));
            headerGo.transform.SetParent(panel, false);
            HeaderArea = headerGo.GetComponent<RectTransform>();
            HeaderArea.anchorMin = new Vector2(0.08f, 0.83f);
            HeaderArea.anchorMax = new Vector2(0.92f, 0.985f);
            HeaderArea.offsetMin = HeaderArea.offsetMax = Vector2.zero;

            TitleText = RewardPanelFactory.CreateText(
                HeaderArea,
                "RewardTitle",
                ResolveTitle(item),
                42,
                TextAnchor.MiddleCenter,
                RewardPanelFactory.PrimaryText);
            RewardPanelFactory.Stretch(TitleText.rectTransform);
            TitleText.verticalOverflow = VerticalWrapMode.Overflow;

            var visualGo = new GameObject(
                "CentralDisplayArea",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RewardVisualHoverTarget));
            visualGo.transform.SetParent(panel, false);
            VisualArea = visualGo.GetComponent<RectTransform>();
            VisualArea.anchorMin = VisualArea.anchorMax =
                new Vector2(0.5f,
                    item.Kind == RewardFeedbackKind.Stat ||
                    item.Kind == RewardFeedbackKind.Unavailable ? 0.5f : 0.43f);
            VisualArea.pivot = new Vector2(0.5f, 0.5f);
            VisualArea.sizeDelta = item.Kind == RewardFeedbackKind.Card
                ? new Vector2(380f, 520f)
                : new Vector2(480f, 480f);
            Image hitArea = visualGo.GetComponent<Image>();
            hitArea.color = Color.clear;
            hitArea.raycastTarget = true;
            visualGo.GetComponent<RewardVisualHoverTarget>().Bind(
                OnVisualHoverChanged,
                RequestVisualSelection,
                PositionTooltipAtScreenPoint);

            Sprite subjectArtwork = RewardArtworkCatalog.ResolveRewardArtwork(item);
            if (subjectArtwork == null)
                subjectArtwork = ResolveStatArtwork(item);

            if (item.Kind == RewardFeedbackKind.Card && item.Card != null)
            {
                CardView card = CardViewFactory.Create(
                    VisualArea,
                    addHoverHandler: false);
                RectTransform cardRect = card.transform as RectTransform;
                cardRect.anchorMin = cardRect.anchorMax =
                    new Vector2(0.5f, 0.5f);
                cardRect.anchoredPosition = Vector2.zero;
                Vector2 reference = cardRect.sizeDelta;
                float scale = Mathf.Min(
                    310f / Mathf.Max(1f, reference.x),
                    490f / Mathf.Max(1f, reference.y));
                cardRect.localScale = Vector3.one * scale;
                card.UpdateCard(
                    CardDisplayData.FromCard(item.Card, canPlay: true),
                    0);
                // “只展示、不可点击”与“灵力不足”是两种状态；按钮单独禁用。
                card.cardButton.interactable = false;
                foreach (Graphic graphic in
                         card.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = false;
                }
                _resultCardTooltip = ResultCardTooltipPresenter.Create(Root.transform);
                _resultCardTooltip.Bind(cardRect, ResultCardTooltipPresenter.BuildText(item.Card));
            }
            else if (item.Kind == RewardFeedbackKind.BaseMana ||
                     item.Kind == RewardFeedbackKind.CurrentHealth && subjectArtwork == null)
            {
                // Mana always uses the same blue qi as its map pickup. A HUD
                // glyph or catalog fallback is never the full reward subject.
                // Healing shares the map's life-drop Sprite when available.
                // Effects owns choreography and palette; this overlay only
                // provides the transparent UI host.
                bool health =
                    item.Kind == RewardFeedbackKind.CurrentHealth;
                var previewObject = new GameObject(
                    health
                        ? "RewardMapHealthPreview"
                        : "RewardMapManaPreview",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage),
                    typeof(RewardMapVfxPreview));
                previewObject.transform.SetParent(VisualArea, false);
                MapVfxPreview =
                    previewObject.GetComponent<RewardMapVfxPreview>();
                MapVfxPreview.Image.material =
                    GetPremultipliedTextureMaterial();
                RectTransform previewRect =
                    previewObject.GetComponent<RectTransform>();
                previewRect.anchorMin = previewRect.anchorMax =
                    new Vector2(0.5f, 0.63f);
                previewRect.pivot = new Vector2(0.5f, 0.5f);
                previewRect.sizeDelta = new Vector2(780f, 780f);
                previewRect.anchoredPosition = Vector2.zero;
            }
            else if (item.Kind == RewardFeedbackKind.Unavailable)
            {
                var status = new GameObject("UnavailableSymbol",
                    typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(RewardUnavailableGraphic));
                status.transform.SetParent(VisualArea, false);
                var graphic = status.GetComponent<RewardUnavailableGraphic>();
                graphic.color = RewardPanelFactory.SecondaryText;
                graphic.raycastTarget = false;
                RectTransform rect = graphic.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(180f, 180f);
            }
            else if (subjectArtwork != null)
            {
                Image artwork = CreateArtwork(
                    VisualArea,
                    subjectArtwork);
                if (item.Kind == RewardFeedbackKind.Stat)
                {
                    // Small pixel glyphs must not be stretched into a 444px relic slot.
                    RectTransform rect = artwork.rectTransform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = Vector2.one * Mathf.Min(256f,
                        Mathf.Max(artwork.sprite.rect.width, artwork.sprite.rect.height) * 2f);
                }
                else if (item.Kind == RewardFeedbackKind.Jade)
                {
                    // Leave a separate quantity slot so custom titles (e.g. boss
                    // cash-out offers) cannot hide the amount being received.
                    RectTransform rect = artwork.rectTransform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = new Vector2(0f, 30f);
                    rect.sizeDelta = new Vector2(360f, 360f);
                }
            }
            else
            {
                Text symbol = RewardPanelFactory.CreateText(
                    VisualArea,
                    "RewardSymbol",
                    string.IsNullOrWhiteSpace(item.Symbol)
                        ? SymbolFor(item.Kind) : item.Symbol,
                    item.Kind == RewardFeedbackKind.Seal ? 180 : 78,
                    TextAnchor.MiddleCenter,
                    item.Accent);
                RewardPanelFactory.Stretch(symbol.rectTransform, 24f);
            }
            if (item.Kind == RewardFeedbackKind.Jade)
            {
                Text quantity = RewardPanelFactory.CreateText(
                    VisualArea, "RewardQuantity", $"+{Math.Max(0, item.Quantity)}",
                    42, TextAnchor.MiddleCenter, RewardPanelFactory.PrimaryText);
                quantity.raycastTarget = false;
                RectTransform rect = quantity.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, -195f);
                rect.sizeDelta = new Vector2(340f, 56f);
            }
            if (item.Kind == RewardFeedbackKind.BaseMana)
            {
                Text quantity = RewardPanelFactory.CreateText(VisualArea,
                    "RewardManaQuantity", $"+{Math.Max(0, item.Quantity)}", 48,
                    TextAnchor.MiddleCenter, RewardPanelFactory.PrimaryText);
                quantity.raycastTarget = false;
                RectTransform amountRect = quantity.rectTransform;
                amountRect.anchorMin = amountRect.anchorMax = amountRect.pivot = new Vector2(0.5f, 0.5f);
                amountRect.anchoredPosition = new Vector2(0f, -178f);
                amountRect.sizeDelta = new Vector2(340f, 60f);
                if (!string.IsNullOrWhiteSpace(item.ProgressText))
                {
                    Text progress = RewardPanelFactory.CreateText(VisualArea,
                        "RewardManaProgress", item.ProgressText, 26,
                        TextAnchor.MiddleCenter, RewardPanelFactory.SecondaryText);
                    progress.raycastTarget = false;
                    RectTransform progressRect = progress.rectTransform;
                    progressRect.anchorMin = progressRect.anchorMax = progressRect.pivot = new Vector2(0.5f, 0.5f);
                    progressRect.anchoredPosition = new Vector2(0f, -239f);
                    progressRect.sizeDelta = new Vector2(620f, 44f);
                }
            }
            // Card results use the same rule tooltip as real cards, not a second
            // reward-name tooltip layered on top. Other reward kinds are unchanged.
            if (_resultCardTooltip == null)
                BuildTooltip(item);
            BuildPrimarySelectionHint(panel, item);
            HeaderArea.SetAsLastSibling();
        }

        private void BuildPrimarySelectionHint(
            RectTransform panel, RewardFeedbackItem item)
        {
            bool hasDecisionHint = !string.IsNullOrWhiteSpace(item.DecisionHint);
            if (!hasDecisionHint && (_canDismiss || _onPrimarySelected == null))
                return;
            _primarySelectionHint = RewardPanelFactory.CreateText(
                panel,
                "PrimarySelectionHint",
                hasDecisionHint ? item.DecisionHint :
                    item.Kind == RewardFeedbackKind.Seal
                        ? "点击灵印，选择卡牌钤印" : "点击中央奖励领取",
                24,
                TextAnchor.MiddleCenter,
                RewardPanelFactory.WarmGold);
            RectTransform rect = _primarySelectionHint.rectTransform;
            // The existing guidance slot also fits a two-line unavailability reason.
            rect.anchorMin = hasDecisionHint
                ? new Vector2(0.08f, 0.015f) : new Vector2(0.12f, 0.025f);
            rect.anchorMax = hasDecisionHint
                ? new Vector2(0.92f, 0.115f) : new Vector2(0.88f, 0.09f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            RefreshPrimarySelectionHint();
        }

        private void RefreshPrimarySelectionHint()
        {
            if (_primarySelectionHint == null)
                return;
            bool hovered = _decisionActionsArmed && _tooltipHovered;
            _primarySelectionHint.color = hovered
                ? RewardPanelFactory.PrimaryText : RewardPanelFactory.WarmGold;
            _primarySelectionHint.fontStyle = hovered
                ? FontStyle.Bold : FontStyle.Normal;
        }

        private static Sprite ResolveStatArtwork(RewardFeedbackItem item)
        {
            if (item == null || item.Kind != RewardFeedbackKind.Stat)
                return null;
            Sprite sprite = RewardArtworkCatalog.Resolve(item.PresentationId);
            if (sprite != null) return sprite;
            string title = item.Title ?? string.Empty;
            if (title.Contains("补牌") || title.Contains("手牌"))
                return Resources.Load<Sprite>(RewardArtworkCatalog.HandResourcePath);
            if (title.Contains("恢复") || title.Contains("生命"))
                return Resources.Load<Sprite>(RewardArtworkCatalog.RecoveryResourcePath);
            if (title.Contains("重选"))
                return Resources.Load<Sprite>(RewardArtworkCatalog.RerollResourcePath);
            if (title.Contains("裂空"))
                return Resources.Load<Sprite>(RewardArtworkCatalog.AreaResourcePath);
            return null;
        }

        private void OnVisualHoverChanged(bool hovered)
        {
            VisualHoverChanged?.Invoke(hovered);
            _tooltipHovered = hovered;
            RefreshPrimarySelectionHint();
            if (_tooltipGroup == null) return;
            if (_tooltipAnimation != null)
            {
                StopCoroutine(_tooltipAnimation);
                _tooltipAnimation = null;
            }
            if (!hovered)
            {
                HideTooltipImmediately();
                return;
            }

            TooltipRoot.gameObject.SetActive(true);
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                _tooltipGroup.alpha = 1f;
                return;
            }

            Canvas.ForceUpdateCanvases();
            PositionTooltipAtPointer();
            _tooltipAnimation = StartCoroutine(AnimateTooltipIn());
        }

        private void LateUpdate()
        {
            if (_tooltipHovered && _tooltipGroup != null &&
                _tooltipGroup.alpha > 0.001f)
            {
                PositionTooltipAtPointer();
            }
        }

        private void HideTooltipImmediately()
        {
            if (_tooltipGroup != null)
                _tooltipGroup.alpha = 0f;
            if (TooltipRoot != null)
                TooltipRoot.gameObject.SetActive(false);
        }

        private IEnumerator AnimateTooltipIn()
        {
            float start = _tooltipGroup.alpha;
            const float duration = 0.10f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _tooltipGroup.alpha = Mathf.Lerp(start, 1f, t);
                yield return null;
            }
            _tooltipGroup.alpha = 1f;
            _tooltipAnimation = null;
        }

        private void BuildTooltip(RewardFeedbackItem item)
        {
            var tooltipGo = new GameObject(
                "RewardTooltip",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Outline),
                typeof(CanvasGroup));
            tooltipGo.transform.SetParent(Root.transform, false);
            TooltipRoot = tooltipGo.GetComponent<RectTransform>();
            TooltipRoot.anchorMin = TooltipRoot.anchorMax =
                new Vector2(0.5f, 0.5f);
            TooltipRoot.pivot = new Vector2(0f, 1f);
            TooltipRoot.anchoredPosition = Vector2.zero;

            Image background = tooltipGo.GetComponent<Image>();
            background.color = new Color(0.075f, 0.07f, 0.06f, 0.98f);
            background.raycastTarget = false;
            Outline outline = tooltipGo.GetComponent<Outline>();
            outline.effectColor = new Color(0.72f, 0.57f, 0.34f, 0.62f);
            outline.effectDistance = new Vector2(2f, -2f);

            TooltipTitleText = RewardPanelFactory.CreateRichText(
                TooltipRoot,
                "TooltipTitle",
                item.DisplayName,
                42,
                TextAlignmentOptions.MidlineLeft,
                RewardPanelFactory.PrimaryText);

            TooltipBodyText = RewardPanelFactory.CreateRichText(
                TooltipRoot,
                "TooltipBody",
                item.Description,
                34,
                TextAlignmentOptions.TopLeft,
                RewardPanelFactory.SecondaryText);

            // 宽度按单行需求收敛、高度按换行行数计算，避免内容少时大片留白。
            const float padX = 26f;
            const float padTop = 12f;
            const float padBottom = 18f;
            const float titleBodyGap = 6f;
            const float maxWidth = 560f;
            const float minWidth = 300f;

            float availableWidth = maxWidth - padX * 2f;
            float titleSingleLineWidth = TooltipTitleText
                .GetPreferredValues(item.DisplayName).x;
            Vector2 bodyWrapped = TooltipBodyText.GetPreferredValues(
                item.Description, availableWidth, 0f);
            float width = Mathf.Clamp(
                Mathf.Max(titleSingleLineWidth, bodyWrapped.x) +
                    padX * 2f,
                minWidth,
                maxWidth);

            if (width - padX * 2f < availableWidth)
                bodyWrapped = TooltipBodyText.GetPreferredValues(
                    item.Description, width - padX * 2f, 0f);
            Vector2 titleWrapped = TooltipTitleText.GetPreferredValues(
                item.DisplayName, width - padX * 2f, 0f);
            float titleHeight = Mathf.Max(52f, titleWrapped.y);
            float bodyHeight = Mathf.Max(44f, bodyWrapped.y);
            TooltipRoot.sizeDelta = new Vector2(
                width,
                padTop + titleHeight + titleBodyGap +
                    bodyHeight + padBottom);

            RectTransform title = TooltipTitleText.rectTransform;
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.offsetMin = new Vector2(padX, -(padTop + titleHeight));
            title.offsetMax = new Vector2(-padX, -padTop);

            RectTransform body = TooltipBodyText.rectTransform;
            body.anchorMin = new Vector2(0f, 1f);
            body.anchorMax = new Vector2(1f, 1f);
            body.offsetMin = new Vector2(
                padX,
                -(padTop + titleHeight + titleBodyGap + bodyHeight));
            body.offsetMax = new Vector2(
                -padX,
                -(padTop + titleHeight + titleBodyGap));

            _tooltipGroup = tooltipGo.GetComponent<CanvasGroup>();
            _tooltipGroup.alpha = 0f;
            _tooltipGroup.blocksRaycasts = false;
            _tooltipGroup.interactable = false;
            TooltipRoot.gameObject.SetActive(false);
        }

        private void PositionTooltipAtPointer()
            => PositionTooltipAtScreenPoint(Input.mousePosition);

        private void PositionTooltipAtScreenPoint(Vector2 screenPoint)
        {
            if (TooltipRoot == null || _canvas == null ||
                _canvasRect == null)
                return;

            Camera eventCamera = _canvas.renderMode ==
                RenderMode.ScreenSpaceOverlay
                ? null
                : _canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect,
                    screenPoint,
                    eventCamera,
                    out Vector2 pointer))
                return;

            Vector2 canvasSize = _canvasRect.rect.size;
            Vector2 tooltipSize = TooltipRoot.rect.size;
            const float pointerOffset = 22f;
            const float edgeInset = 18f;
            bool placeLeft = pointer.x + pointerOffset + tooltipSize.x >
                canvasSize.x * 0.5f - edgeInset;
            bool placeAbove = pointer.y - pointerOffset - tooltipSize.y <
                -canvasSize.y * 0.5f + edgeInset;

            TooltipRoot.pivot = new Vector2(
                placeLeft ? 1f : 0f,
                placeAbove ? 0f : 1f);
            Vector2 position = pointer + new Vector2(
                placeLeft ? -pointerOffset : pointerOffset,
                placeAbove ? pointerOffset : -pointerOffset);

            float minX = -canvasSize.x * 0.5f + edgeInset;
            float maxX = canvasSize.x * 0.5f - edgeInset;
            float minY = -canvasSize.y * 0.5f + edgeInset;
            float maxY = canvasSize.y * 0.5f - edgeInset;
            if (TooltipRoot.pivot.x < 0.5f)
                position.x = Mathf.Min(position.x, maxX - tooltipSize.x);
            else
                position.x = Mathf.Max(position.x, minX + tooltipSize.x);
            if (TooltipRoot.pivot.y > 0.5f)
                position.y = Mathf.Max(position.y, minY + tooltipSize.y);
            else
                position.y = Mathf.Min(position.y, maxY - tooltipSize.y);
            if (_primarySelectionHint != null)
            {
                // 长规则说明不能遮住主入口提示；使用实际面板动画位置，
                // 仍以画布边界为约束，不改变主体的点击热区。
                Bounds hintBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                    _canvasRect, _primarySelectionHint.rectTransform);
                var hintRect = new Rect((Vector2)hintBounds.min, (Vector2)hintBounds.size);
                var tooltipRect = new Rect(
                    position - Vector2.Scale(TooltipRoot.pivot, tooltipSize), tooltipSize);
                if (tooltipRect.Overlaps(hintRect))
                {
                    float aboveBottom = hintBounds.max.y + pointerOffset;
                    float belowTop = hintBounds.min.y - pointerOffset;
                    if (aboveBottom + tooltipSize.y <= maxY)
                        position.y = aboveBottom + tooltipSize.y * TooltipRoot.pivot.y;
                    else if (belowTop - tooltipSize.y >= minY)
                        position.y = belowTop - tooltipSize.y * (1f - TooltipRoot.pivot.y);
                }
            }
            TooltipRoot.anchoredPosition = position;
            TooltipRoot.SetAsLastSibling();
        }

        private IEnumerator AnimateSubjectIn()
        {
            if (VisualArea == null) yield break;
            const float duration = 0.28f;
            float elapsed = 0f;
            VisualArea.localScale = Vector3.one * 0.88f;
            while (elapsed < duration && VisualArea != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float scale = t < 0.72f
                    ? Mathf.Lerp(0.88f, 1.035f, t / 0.72f)
                    : Mathf.Lerp(1.035f, 1f, (t - 0.72f) / 0.28f);
                VisualArea.localScale = Vector3.one * scale;
                yield return null;
            }
            if (VisualArea != null)
                VisualArea.localScale = Vector3.one;
            _resultCardTooltip?.EnableForResultHold();
            _visualAnimation = null;
        }

        private static Image CreateArtwork(
            RectTransform parent,
            Sprite sprite)
        {
            var go = new GameObject(
                "RewardArtwork",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            RewardPanelFactory.Stretch(image.rectTransform, 18f);
            return image;
        }

        private static string SymbolFor(RewardFeedbackKind kind)
        {
            return kind switch
            {
                RewardFeedbackKind.BaseMana => string.Empty,
                RewardFeedbackKind.CurrentHealth => string.Empty,
                RewardFeedbackKind.Jade => "玉",
                RewardFeedbackKind.Relic => "珍",
                RewardFeedbackKind.Seal => "印",
                RewardFeedbackKind.Stat => "升",
                _ => "得"
            };
        }

        private static string ResolveTitle(RewardFeedbackItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Title))
                return item.Title;

            return item.Kind switch
            {
                RewardFeedbackKind.Card => "获得卡牌",
                RewardFeedbackKind.BaseMana => "获得灵力",
                RewardFeedbackKind.CurrentHealth => "恢复生命",
                RewardFeedbackKind.Jade => "获得灵玉",
                RewardFeedbackKind.Relic => string.IsNullOrWhiteSpace(
                        item.DisplayName)
                    ? "获得遗珍"
                    : $"获得遗珍 · {item.DisplayName}",
                RewardFeedbackKind.Seal => "获得灵印",
                RewardFeedbackKind.Stat => "获得成长",
                _ => "获得奖励"
            };
        }

        private void OnDestroy()
        {
            Close(invokeCallback: false);
            if (_premultipliedTextureMaterial != null)
            {
                Destroy(_premultipliedTextureMaterial);
                _premultipliedTextureMaterial = null;
            }
        }

        private Material GetPremultipliedTextureMaterial()
        {
            if (_premultipliedTextureMaterial != null)
                return _premultipliedTextureMaterial;

            Shader shader = Resources.Load<Shader>(
                "Shaders/PremultipliedTextureUI");
            if (shader == null)
                shader = Shader.Find(
                    "WanjieScroll/UI/Premultiplied Texture");
            if (shader == null)
            {
                if (!_reportedMissingPremultipliedShader)
                {
                    Debug.LogWarning(
                        "Reward reveal premultiplied UI shader is missing; " +
                        "render-texture particles may appear too faint.",
                        this);
                    _reportedMissingPremultipliedShader = true;
                }
                return null;
            }

            _premultipliedTextureMaterial = new Material(shader)
            {
                name = "Reward Premultiplied Texture Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            return _premultipliedTextureMaterial;
        }
    }

    /// <summary>
    /// UI-side render target for a map-semantic reward visual. Bootstrap owns
    /// the actual Effects instance so UI keeps no dependency on Effects.
    /// </summary>
    public sealed class RewardMapVfxPreview : MonoBehaviour
    {
        public RawImage Image { get; private set; }

        private void Awake()
        {
            Image = GetComponent<RawImage>();
            Image.color = Color.clear;
            Image.raycastTarget = false;
        }

        public void SetTexture(Texture texture)
        {
            if (Image == null)
                Image = GetComponent<RawImage>();
            Image.texture = texture;
            Image.color = texture != null ? Color.white : Color.clear;
        }
    }

    internal sealed class RewardVisualHoverTarget : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler,
        IPointerMoveHandler
    {
        private Action<bool> _onHover;
        private Action _onClick;
        private Action<Vector2> _onPointerMoved;

        public void Bind(
            Action<bool> onHover,
            Action onClick,
            Action<Vector2> onPointerMoved)
        {
            _onHover = onHover;
            _onClick = onClick;
            _onPointerMoved = onPointerMoved;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            _onPointerMoved?.Invoke(eventData.position);
            _onHover?.Invoke(true);
        }

        public void OnPointerExit(PointerEventData eventData)
            => _onHover?.Invoke(false);

        public void OnPointerClick(PointerEventData eventData)
            => _onClick?.Invoke();

        public void OnPointerMove(PointerEventData eventData)
            => _onPointerMoved?.Invoke(eventData.position);

        private void OnDisable()
        {
            _onHover?.Invoke(false);
        }
    }
}
