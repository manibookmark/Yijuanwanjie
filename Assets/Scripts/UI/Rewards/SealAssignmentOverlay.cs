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
    /// 已确定灵印后的宿主分配面板。通用 RewardRevealOverlay 负责“获得了什么”，
    /// 本组件只接管奖励主体向右上待钤锚点的连续过渡、合法卡墙与提交意图。
    /// 点击合法卡牌即提交，不保留二次确认态。
    /// 规模审查：本文件超过 800 行，保留于此因其仍只拥有“宿主分配面板”的一个
    /// 生命周期；提交后卡牌 Transform、视频与收牌等待均由
    /// SealStampPresentation 唯一写入；成果 Hover 的命中与 Tooltip 生命周期
    /// 由 ResultCardTooltipPresenter 独立拥有，面板只开放短暂停留时的悬停。
    /// 暂停/恢复与待交付回执仍归该面板 owner，不另建透传层。
    /// 若再加入来源特定返回状态或奖励候选
    /// 生成，必须重新评估并提取真实状态所有者。
    /// 停用恢复仍属于同一面板生命周期：保留原候选与提交门禁，不重建
    /// 奖励、不新增业务状态；已提交演出的中断在宿主恢复后只释放原展示等待。
    /// </summary>
    public sealed class SealAssignmentOverlay : MonoBehaviour
    {
        public GameObject Root { get; private set; }
        public bool IsShown => Root != null;

        private const float CandidateBaseScale = 1f;
        private const float IntroFlyDuration = 0.42f;

        private readonly List<CardView> _cards = new List<CardView>();
        private readonly List<CanvasGroup> _cardGroups = new List<CanvasGroup>();
        private readonly List<RectTransform> _cardSlots =
            new List<RectTransform>();

        private SealAssignmentViewModel _viewModel;
        private CanvasGroup _rootGroup;
        private RectTransform _panel;
        private Image _panelImage;
        private Image _scrimImage;
        private CanvasGroup _selectionGroup;
        private Button _backButton;
        private Text _title;
        private Text _resultText;
        private RectTransform _sealArtwork;
        private RectTransform _sealAnchor;
        private RectTransform _resultRail;
        private RectTransform _committedCardSlot;
        private Action<int> _onConfirm;
        private Action _onCancel;
        private Action _committedCompletion;
        private Action _resumeCommittedCompletion;
        private int _resumeCommittedGeneration;
        private Coroutine _transition;
        private SealStampPresentation _stampPresentation;
        private ResultCardTooltipPresenter _resultCardTooltip;
        private bool _selectionVisible;
        private bool _submissionPending;
        private bool _committedPlaying;
        private int _presentationGeneration;
        private bool _suspended;

        public void Show(
            Canvas canvas,
            SealAssignmentViewModel viewModel,
            Action<int> onConfirm,
            Action onCancel)
        {
            int expectedGeneration = unchecked(_presentationGeneration + 1);
            CloseSilently();
            // Closing a committed predecessor can synchronously enter a newer
            // flow. That newer owner wins instead of being overwritten here.
            if (_presentationGeneration != expectedGeneration) return;
            if (canvas == null)
                throw new ArgumentNullException(nameof(canvas));
            _viewModel = viewModel ??
                throw new ArgumentNullException(nameof(viewModel));
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            Root = RewardPanelFactory.CreateRoot(
                canvas,
                "SealAssignmentOverlay",
                false,
                out _rootGroup,
                out _);
            _scrimImage = Root.GetComponent<Image>();
            _panel = RewardPanelFactory.CreatePanel(
                Root.transform,
                "SealAssignmentPanel",
                new Vector2(1660f, 860f),
                out _);
            _panelImage = _panel.GetComponent<Image>();
            _panel.localScale = Vector3.one;

            _title = RewardPanelFactory.CreateHeader(
                _panel,
                "选择一张牌钤印");
            BuildSelection(_panel);
            BuildFixedSealAnchor(_resultRail);
            BuildTransitionArtwork(_panel);
            SetSelectionVisible(false);
            _stampPresentation = _panel.gameObject.AddComponent<
                SealStampPresentation>();
            ResumeSelectionEntrance();
        }

        /// <summary>
        /// Inspector 调参专用入口：同步完成候选页搭建但不把它渲染出来，
        /// 直接从最近一次真实选择进入落印、命中粒子与成果卡消散。
        /// </summary>
        public bool ShowCommittedPreview(
            Canvas canvas,
            SealAssignmentViewModel viewModel,
            int selectedIndex,
            Action onPlaybackStarted,
            Action onClosed,
            Action onEvolutionRevealed = null)
        {
            if (!isActiveAndEnabled || viewModel == null || !viewModel.Select(selectedIndex))
                return false;

            int expectedGeneration = unchecked(_presentationGeneration + 1);
            Show(canvas, viewModel, null, CloseSilently);
            if (_presentationGeneration != expectedGeneration ||
                !ReferenceEquals(_viewModel, viewModel)) return false;
            if (_transition != null)
            {
                StopCoroutine(_transition);
                _transition = null;
            }
            if (_rootGroup != null)
                _rootGroup.alpha = 1f;
            if (_panel != null)
                _panel.localScale = Vector3.one;
            _submissionPending = true;
            RefreshSelectedCard();
            PlayCommitted(onPlaybackStarted, onClosed, onEvolutionRevealed);
            return true;
        }

        /// <summary>
        /// 后续视频层只读取此坐标，不能复制一套右上角偏移，从而保证 UI 图标与
        /// 视频首帧严格衔接。
        /// </summary>
        public bool TryGetAnimationOriginScreenPoint(out Vector2 screenPoint)
        {
            if (_sealAnchor == null)
            {
                screenPoint = default;
                return false;
            }
            Canvas canvas = Root != null
                ? Root.GetComponentInParent<Canvas>()
                : null;
            Camera camera = canvas != null &&
                            canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            Vector3 worldCenter = _sealAnchor.TransformPoint(
                _sealAnchor.rect.center);
            screenPoint = RectTransformUtility.WorldToScreenPoint(
                camera, worldCenter);
            return true;
        }

        private void BuildFixedSealAnchor(RectTransform rail)
        {
            _sealAnchor = CreateRect(
                rail,
                "SealAnimationOrigin",
                new Vector2(0.5f, 0.805f),
                new Vector2(0.5f, 0.805f));
            _sealAnchor.pivot = new Vector2(0.5f, 0.5f);
            _sealAnchor.anchoredPosition = Vector2.zero;
            _sealAnchor.sizeDelta = new Vector2(96f, 96f);
        }

        private void BuildTransitionArtwork(RectTransform panel)
        {
            var artworkGo = new GameObject(
                "SealArtwork",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            artworkGo.transform.SetParent(panel, false);
            Image artwork = artworkGo.GetComponent<Image>();
            artwork.sprite = RewardArtworkCatalog.Resolve(
                _viewModel.Seal.presentationId);
            artwork.preserveAspect = true;
            artwork.raycastTarget = false;
            // Image 没有 Sprite 时会画出默认白块；缺图灵印沿用正式印字，
            // 并让印字跟随同一个主体向待钤锚点过渡。
            artwork.enabled = artwork.sprite != null;
            _sealArtwork = artwork.rectTransform;
            _sealArtwork.anchorMin = _sealArtwork.anchorMax =
                new Vector2(0.5f, 0.46f);
            _sealArtwork.pivot = new Vector2(0.5f, 0.5f);
            _sealArtwork.sizeDelta = new Vector2(420f, 420f);
            if (artwork.sprite == null)
            {
                Text symbol = RewardPanelFactory.CreateText(
                    _sealArtwork,
                    "SealSymbol",
                    string.IsNullOrWhiteSpace(_viewModel.Seal.sealCharacter)
                        ? "印" : _viewModel.Seal.sealCharacter,
                    210,
                    TextAnchor.MiddleCenter,
                    new Color(0.58f, 0.48f, 0.72f, 1f));
                symbol.resizeTextForBestFit = true;
                symbol.resizeTextMinSize = 20;
                symbol.resizeTextMaxSize = 210;
                RewardPanelFactory.Stretch(symbol.rectTransform, 8f);
            }
        }

        private void BuildSelection(RectTransform panel)
        {
            RectTransform selection = CreateRect(
                panel, "Assignment", Vector2.zero, Vector2.one);
            _selectionGroup = selection.gameObject.AddComponent<CanvasGroup>();
            BuildCandidates(selection);
            BuildRightRail(selection);
        }

        private void BuildCandidates(RectTransform selection)
        {
            RectTransform viewport = CreateRect(
                selection,
                "CandidateViewport",
                RewardPanelFactory.CandidateAreaMin,
                RewardPanelFactory.CandidateAreaMax);
            var maskImage = viewport.gameObject.AddComponent<Image>();
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

            var layout = content.gameObject.AddComponent<GridLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 16, 24);
            layout.cellSize = new Vector2(276f, 382f);
            layout.spacing = new Vector2(18f, 24f);
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;

            CardSealSpec preview = _viewModel.Seal.ToCardSealSpec();
            for (int i = 0; i < _viewModel.Candidates.Count; i++)
            {
                int captured = i;
                var slot = new GameObject(
                    $"Candidate_{i}", typeof(RectTransform));
                slot.transform.SetParent(content, false);
                RectTransform slotRect = slot.GetComponent<RectTransform>();
                _cardSlots.Add(slotRect);

                var group = slot.AddComponent<CanvasGroup>();
                _cardGroups.Add(group);

                CardView card = CardViewFactory.Create(
                    slot.transform, addHoverHandler: false);
                RectTransform cardRect = card.transform as RectTransform;
                cardRect.anchorMin = cardRect.anchorMax =
                    new Vector2(0.5f, 0.5f);
                cardRect.anchoredPosition = Vector2.zero;
                cardRect.localScale = Vector3.one * CandidateBaseScale;

                var hover = slot.AddComponent<UI.Components.HoverScaleEffect>();
                hover.NormalScale = 1f;
                hover.HoverScale = 1.045f;

                card.UpdateCard(_viewModel.BuildCandidateDisplayData(i), i);
                card.SetSealAssignmentPreview(true, -1, preview);
                card.cardButton.onClick.AddListener(
                    () => SelectAndSubmit(captured));
                _cards.Add(card);
            }
        }

        private void BuildRightRail(RectTransform selection)
        {
            _resultRail = RewardPanelFactory.CreateResultRail(
                selection,
                "SealResultRail",
                "待钤灵印");

            Text sealName = RewardPanelFactory.CreateText(
                _resultRail,
                "PendingSealName",
                _viewModel.Seal.displayName,
                27,
                TextAnchor.MiddleCenter,
                RewardPanelFactory.PrimaryText);
            Place(sealName.rectTransform,
                new Vector2(0.08f, 0.65f), new Vector2(0.92f, 0.73f));

            Text grade = RewardPanelFactory.CreateText(
                _resultRail,
                "PendingSealGrade",
                _viewModel.GradeDisplayName,
                18,
                TextAnchor.MiddleCenter,
                RewardPanelFactory.WarmGold);
            Place(grade.rectTransform,
                new Vector2(0.10f, 0.59f), new Vector2(0.90f, 0.65f));

            Text detail = RewardPanelFactory.CreateText(
                _resultRail,
                "PendingSealDescription",
                NormalizeMultiplierText(_viewModel.Seal.description),
                21,
                TextAnchor.UpperCenter,
                RewardPanelFactory.SecondaryText);
            Place(detail.rectTransform,
                new Vector2(0.08f, 0.41f), new Vector2(0.92f, 0.56f));

            _resultText = RewardPanelFactory.CreateText(
                _resultRail,
                "AssignmentHint",
                "点击卡牌，立即钤印",
                22,
                TextAnchor.MiddleCenter,
                RewardPanelFactory.JadeHighlight);
            Place(_resultText.rectTransform,
                new Vector2(0.06f, 0.23f), new Vector2(0.94f, 0.38f));

            RewardPanelFactory.CreateRailActionDivider(
                _resultRail,
                0.19f);

            _backButton = RewardPanelFactory.CreateButton(
                _resultRail,
                "BackButton",
                "返回",
                RewardPanelFactory.NeutralAction);
            RectTransform backRect = _backButton.transform as RectTransform;
            backRect.anchorMin = backRect.anchorMax =
                new Vector2(0.5f, 0.08f);
            backRect.pivot = new Vector2(0.5f, 0.5f);
            backRect.sizeDelta = new Vector2(232f, 56f);
            _backButton.onClick.AddListener(Cancel);
        }

        private static string NormalizeMultiplierText(string value)
        {
            string result = value ?? string.Empty;
            for (int digit = 0; digit <= 9; digit++)
            {
                string suffix = digit.ToString();
                result = result.Replace("x" + suffix, "×" + suffix)
                    .Replace("X" + suffix, "×" + suffix);
            }
            return result;
        }

        private void ResumeSelectionEntrance()
        {
            if (Root == null) return;
            if (!isActiveAndEnabled)
            {
                SuspendPresentation();
                return;
            }
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            _suspended = false;
            Root.SetActive(true);
            _rootGroup.alpha = 0f;
            _rootGroup.interactable = _rootGroup.blocksRaycasts = true;
            _panel.localScale = Vector3.one;
            _selectionGroup.gameObject.SetActive(true);
            SetSelectionVisible(false);
            _sealArtwork.SetParent(_panel, false);
            _sealArtwork.anchorMin = _sealArtwork.anchorMax = new Vector2(0.5f, 0.46f);
            _sealArtwork.pivot = new Vector2(0.5f, 0.5f);
            _sealArtwork.anchoredPosition = Vector2.zero;
            _sealArtwork.sizeDelta = new Vector2(420f, 420f);
            _sealArtwork.localRotation = Quaternion.identity;
            _sealArtwork.localScale = Vector3.one;
            _sealArtwork.gameObject.SetActive(true);
            _stampPresentation.Prepare(_panel);
            _transition = StartCoroutine(AnimateSealToAnchor(_presentationGeneration, Root));
        }

        private IEnumerator AnimateSealToAnchor(int generation, GameObject root)
        {
            Canvas.ForceUpdateCanvases();
            _sealArtwork.SetParent(_panel, true);
            Vector3 startPosition = _sealArtwork.position;
            Vector3 targetPosition = _sealAnchor.TransformPoint(
                _sealAnchor.rect.center);
            Vector2 startSize = _sealArtwork.rect.size;
            Vector2 targetSize = _sealAnchor.rect.size;

            for (float elapsed = 0f; elapsed < IntroFlyDuration;
                 elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / IntroFlyDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                _sealArtwork.position = Vector3.Lerp(
                    startPosition, targetPosition, eased);
                _sealArtwork.sizeDelta = Vector2.Lerp(
                    startSize, targetSize, eased);
                _sealArtwork.localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.Lerp(0f, -5f, eased));
                if (_rootGroup != null)
                    _rootGroup.alpha = Mathf.Clamp01(t / 0.24f);
                _panel.localScale = Vector3.one * Mathf.Lerp(
                    0.96f,
                    1f,
                    eased);
                _selectionGroup.alpha = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.28f, 0.92f, t));
                yield return null;
            }

            if (generation != _presentationGeneration || Root != root) yield break;
            _sealArtwork.SetParent(_sealAnchor, false);
            Place(_sealArtwork, Vector2.zero, Vector2.one);
            _sealArtwork.localRotation = Quaternion.identity;
            if (_rootGroup != null)
                _rootGroup.alpha = 1f;
            _panel.localScale = Vector3.one;
            SetSelectionVisible(true);
            _transition = null;
        }

        private void SelectAndSubmit(int index)
        {
            if (!_selectionVisible || _submissionPending ||
                _committedPlaying || !_viewModel.Select(index))
                return;

            _submissionPending = true;
            RefreshSelectedCard();
            SetInteractable(false);
            SealAssignmentCandidate selected = _viewModel.Selected;
            _resultText.text = _viewModel.Seal.effectType == CardSealEffect.Evolution
                ? $"正在化形\n\n{selected.Card.cardName} → {selected.Card.evolutionTarget.cardName}" +
                  $"\n伤害：{selected.BeforeDamage} → {selected.AfterDamage}\n消耗一个空印位，不消耗其他卡牌"
                : $"正在钤印\n\n伤害：{selected.BeforeDamage} → " +
                  $"{selected.AfterDamage}\n印位：" +
                  $"{selected.TargetSlotIndex + 1} / " +
                  $"{CardSealRules.GetSlotCapacity(selected.Card)}";
            _onConfirm?.Invoke(selected.Card.runtimeInstanceId);
        }

        private void RefreshSelectedCard()
        {
            CardSealSpec preview = _viewModel.Seal.ToCardSealSpec();
            for (int i = 0; i < _cards.Count; i++)
            {
                bool selected = _viewModel.SelectedIndex == i;
                bool previewsDamage =
                    _viewModel.Seal.effectType == CardSealEffect.DoubleDamage ||
                    _viewModel.Seal.effectType == CardSealEffect.TripleDamage;
                CardDisplayData display = selected && previewsDamage
                    ? _viewModel.BuildCommittedDisplayData()
                    : _viewModel.BuildCandidateDisplayData(i);
                _cards[i].UpdateCard(display, i);
                _cards[i].SetSelected(selected);
                _cards[i].SetSealAssignmentPreview(
                    true,
                    selected ? _viewModel.Candidates[i].TargetSlotIndex : -1,
                    preview);
                _cardGroups[i].alpha = selected ? 1f : 0.45f;
            }
        }

        private void Cancel()
        {
            if (_committedPlaying || _submissionPending)
                return;
            Action callback = _onCancel;
            CloseSilently();
            callback?.Invoke();
        }

        public void PlayCommitted(
            Action onPlaybackStarted,
            Action onClosed,
            Action onEvolutionRevealed = null,
            SealCardReturnTarget? returnTarget = null)
        {
            if (Root == null)
            {
                if (isActiveAndEnabled)
                    onClosed?.Invoke();
                else if (_resumeCommittedCompletion == null)
                {
                    _resumeCommittedCompletion = onClosed;
                    _resumeCommittedGeneration = _presentationGeneration;
                }
                return;
            }
            if (_committedPlaying)
                return;

            _resultCardTooltip?.Hide();
            SetInteractable(false);
            _committedPlaying = true;
            _committedCompletion = onClosed;
            if (!isActiveAndEnabled || !Root.activeInHierarchy)
            {
                // The card is committed, but its caller may be disabled too.
                // Deliver the original wait only after this host is enabled.
                if (!isActiveAndEnabled)
                    DeferCommittedCompletion();
                else
                    CloseSilently();
                return;
            }
            bool isEvolution = _viewModel.Seal.effectType == CardSealEffect.Evolution;
            _title.text = isEvolution ? "正在化形" : "正在钤印";
            int selectedIndex = _viewModel.SelectedIndex;
            CardView selectedCard = selectedIndex >= 0 &&
                                    selectedIndex < _cards.Count
                ? _cards[selectedIndex]
                : null;
            RectTransform selectedSlot = selectedIndex >= 0 &&
                                         selectedIndex < _cardSlots.Count
                ? _cardSlots[selectedIndex]
                : null;
            _committedCardSlot = selectedSlot;
            if (selectedSlot != null)
            {
                // 成果演出接管该槽的缩放，停止选卡悬停组件争写同一 Transform。
                var hover = selectedSlot.GetComponent<UI.Components.HoverScaleEffect>();
                if (hover != null) hover.enabled = false;
            }
            if (selectedCard != null)
            {
                selectedCard.UpdateCard(
                    _viewModel.BuildCandidateDisplayData(selectedIndex),
                    selectedIndex);
                selectedCard.SetSealAssignmentPreview(false, -1, null);
            }
            if (_sealArtwork != null)
                _sealArtwork.gameObject.SetActive(false);

            _stampPresentation ??= _panel.gameObject.AddComponent<
                SealStampPresentation>();
            var context = new SealStampVisualContext(
                _panel,
                selectedSlot,
                selectedCard,
                _cardGroups,
                selectedIndex,
                _viewModel.BuildCommittedDisplayData(),
                isEvolution,
                returnTarget);
            int generation = _presentationGeneration;
            GameObject committedRoot = Root;
            _stampPresentation.Play(
                context,
                () =>
                {
                    if (!IsCurrentCommittedPresentation(generation, committedRoot)) return;
                    ShowCommittedCardOnly();
                    onPlaybackStarted?.Invoke();
                },
                () =>
                {
                    if (!IsCurrentCommittedPresentation(generation, committedRoot)) return;
                    OnCommittedResultShown();
                    if (!returnTarget.HasValue)
                        EnableCommittedResultTooltip();
                    if (isEvolution)
                        onEvolutionRevealed?.Invoke();
                },
                () =>
                {
                    if (IsCurrentCommittedPresentation(generation, committedRoot))
                        _resultCardTooltip?.Hide();
                },
                () => FinishCommittedPresentation(generation, committedRoot),
                () => FinishCommittedPresentation(generation, committedRoot));
        }

        private void ShowCommittedCardOnly()
        {
            if (!_committedPlaying)
                return;

            // 演出组件已先把选中卡脱离候选层；演出启动后立即撤掉窗口
            // 内容。Root 继续隔离输入，视频与粒子层由表现组件自行持有。
            if (_panelImage != null)
                _panelImage.enabled = false;
            if (_scrimImage != null)
                _scrimImage.color = Color.clear;
            if (_title != null)
                _title.gameObject.SetActive(false);
            if (_selectionGroup != null)
                _selectionGroup.gameObject.SetActive(false);

            for (int index = 0; index < _panel.childCount; index++)
            {
                GameObject child = _panel.GetChild(index).gameObject;
                if ((_committedCardSlot != null &&
                     child == _committedCardSlot.gameObject) ||
                    (_stampPresentation != null &&
                     _stampPresentation.IsPresentationVisual(child)))
                {
                    continue;
                }
                child.SetActive(false);
            }
        }

        private void OnCommittedResultShown()
        {
            // 结果帧再次收束可见层，防止演出期间后来创建的窗口内容残留。
            ShowCommittedCardOnly();
        }

        private void EnableCommittedResultTooltip()
        {
            if (!_committedPlaying)
                return;
            int selectedIndex = _viewModel.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _cards.Count)
            {
                _resultCardTooltip ??= ResultCardTooltipPresenter.Create(Root.transform);
                _resultCardTooltip.Bind(_cards[selectedIndex].transform as RectTransform,
                    _viewModel.BuildCommittedTooltipText());
                _resultCardTooltip.EnableForResultHold();
            }
        }

        private bool IsCurrentCommittedPresentation(int generation, GameObject root)
        {
            return this != null && generation == _presentationGeneration &&
                   Root == root && _committedPlaying;
        }

        private void FinishCommittedPresentation(int generation, GameObject root)
        {
            if (!IsCurrentCommittedPresentation(generation, root))
                return;
            // The domain commit already happened. Both a normal finish and a
            // lost view release this presentation wait; neither submits again.
            if (!isActiveAndEnabled)
                DeferCommittedCompletion();
            else
                CloseSilently();
        }

        private void SetSelectionVisible(bool value)
        {
            _selectionVisible = value;
            _selectionGroup.alpha = value ? 1f : 0f;
            _selectionGroup.blocksRaycasts = value;
            _selectionGroup.interactable = value;
            SetInteractable(value && !_submissionPending && !_committedPlaying);
        }

        private void SetInteractable(bool value)
        {
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].cardButton.interactable = value;
            if (_backButton != null)
                _backButton.interactable = value;
        }

        public void CloseSilently()
        {
            unchecked { _presentationGeneration++; }
            // Explicit close/replacement cancels a deferred predecessor. It must
            // never release a newer reward flow when this host later resumes.
            _resumeCommittedCompletion = null;
            GameObject root = Root;
            Root = null;
            SealStampPresentation previousStamp = _stampPresentation;
            _stampPresentation = null;
            if (previousStamp != null)
                previousStamp.CancelSilently();
            _resultCardTooltip?.Hide();
            _resultCardTooltip = null;
            if (_transition != null)
                StopCoroutine(_transition);
            // Destroy 延迟到帧末；先同步撤销全屏点击面的射线，避免最后一帧
            // 继续吞掉战斗中的怪物 Tooltip 与手牌 Hover。
            if (_rootGroup != null)
            {
                _rootGroup.interactable = false;
                _rootGroup.blocksRaycasts = false;
            }
            if (_selectionGroup != null)
            {
                _selectionGroup.interactable = false;
                _selectionGroup.blocksRaycasts = false;
            }
            Action committedCompletion = _committedCompletion;
            _committedCompletion = null;
            _transition = null;
            _committedPlaying = false;
            _cards.Clear();
            _cardGroups.Clear();
            _cardSlots.Clear();
            _viewModel = null;
            _rootGroup = null;
            _panel = null;
            _panelImage = null;
            _scrimImage = null;
            _selectionGroup = null;
            _backButton = null;
            _title = null;
            _resultText = null;
            _sealArtwork = null;
            _sealAnchor = null;
            _resultRail = null;
            _committedCardSlot = null;
            _stampPresentation = null;
            _onConfirm = null;
            _onCancel = null;
            _selectionVisible = false;
            _submissionPending = false;
            _suspended = false;
            if (root != null)
            {
                root.SetActive(false);
                Destroy(root);
            }
            committedCompletion?.Invoke();
        }

        private void DeferCommittedCompletion()
        {
            Action completion = _committedCompletion;
            _committedCompletion = null;
            CloseSilently();
            _resumeCommittedCompletion = completion;
            _resumeCommittedGeneration = _presentationGeneration;
        }

        private void SuspendPresentation()
        {
            GameObject root = Root;
            if (root == null) return;
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            _resultCardTooltip?.Hide();
            if (_committedPlaying)
            {
                // Cancel the visual now; a disabled caller cannot consume its
                // original interruption receipt until this host resumes.
                DeferCommittedCompletion();
                return;
            }
            _suspended = true;
            SetSelectionVisible(false);
            _rootGroup.interactable = _rootGroup.blocksRaycasts = false;
            root.SetActive(false);
        }

        private void OnDisable() => SuspendPresentation();

        private void OnEnable()
        {
            Action completion = _resumeCommittedGeneration == _presentationGeneration
                ? _resumeCommittedCompletion : null;
            _resumeCommittedCompletion = null;
            // Clear first: the receipt may synchronously open another reward.
            completion?.Invoke();
            if (_suspended && !_committedPlaying) ResumeSelectionEntrance();
        }

        private void OnDestroy()
        {
            // Whole-flow teardown owns destruction; no disabled caller is
            // revived and no committed presentation is reported as complete.
            _committedCompletion = null;
            CloseSilently();
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
    }
}
