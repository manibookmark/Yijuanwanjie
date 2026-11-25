using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI;
using WanjieScroll.UI.Cards;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 手牌布局系统：扇形排列 + 弧形弯曲 + 旋转。
    /// 对象池/出牌动画/灰显委托给 CardViewPool / CardPlaySequencer / CardPlayabilityController。
    /// 模块审查：手牌身份到可复用槽位的映射与生成牌待入场 ID 由本模块持有；
    /// 动画仅回报固定 ID，不决定槽位身份。抽牌队列、冻结卡面与自动使用
    /// 衔接归 HandDrawPresentation；生成牌代理归 GeneratedCardPresentation。
    /// 本视图仅合成到达显隐，不复制两个 owner 的动画状态。
    /// </summary>
    public class HandView : UIView
    {
        /// <summary>
        /// Requests semantic draw-arrival feedback without coupling UI to Audio.
        /// The scene composition root decides how that intent is presented.
        /// </summary>
        public event System.Action CardDrawFeedbackRequested;

        [Header("布局参数")]
        public RectTransform cardContainer;
        [Range(10f, 80f)] public float fanAngle = 50f;
        [Range(100f, 400f)] public float fanRadius = 280f;
        [Range(0.3f, 1f)] public float cardScale = 0.75f;
        [Range(30f, 150f)] public float cardSpacing = 80f;
        [Range(0f, 25f)] public float maxRotation = 15f;

        [Header("卡牌组件")]
        public CardView cardPrefab;
        public CardPresentationProfile cardPresentation;

        [Header("排序按钮")]
        public Button sortByCostButton;
        public Button sortByColorButton;
        public Button sortDefaultButton;

        [Header("牌堆计数")]
        public Text deckCountText;
        public Text discardCountText;

        [Header("出牌堆")]
        public PlayPileView playPileView;

        private HandViewModel _handVM;
        private CardViewPool _pool;
        private CardPlaySequencer _playSequencer;
        private CardPlayabilityController _playability;
        private HandDrawPresentation _drawPresentation;
        private HandCostChangePresentation _costFeedback;
        private bool _interactionEnabled = true;
        private readonly HashSet<int> _pendingGeneratedIds = new HashSet<int>();
        private GeneratedCardPresentation _generatedPresentation;
        private int _bindingGeneration;
        private System.Action _resumePresentationComplete;

        public bool IsCardPresentationActive =>
            (_playSequencer != null && _playSequencer.IsPresentationActive) ||
            (_drawPresentation != null && _drawPresentation.IsActive) ||
            (_generatedPresentation != null && _generatedPresentation.IsActive);
        public bool IsInteractionEnabled => _interactionEnabled;

        public void SetPresentationPaused(bool paused)
        {
            if (_playSequencer != null)
                _playSequencer.IsPaused = paused;
        }

        protected override void OnViewModelChanged(string propertyName)
        {
            if (propertyName == "*" || propertyName == HandViewModel.PROP_HAND)
                RefreshHand();
            if (propertyName == "*" || propertyName == HandViewModel.PROP_PILE_COUNTS)
                RefreshPileCounts();
        }

        public override void Bind(ViewModelBase viewModel)
        {
            base.Bind(viewModel);
            _handVM = viewModel as HandViewModel;
            if (_handVM != null) _handVM.CardAutomaticallyPlayed += OnCardAutomaticallyPlayed;
            if (sortByCostButton != null) sortByCostButton.onClick.AddListener(() => _handVM.SortByCost());
            if (sortByColorButton != null) sortByColorButton.onClick.AddListener(() => _handVM.SortByColor());
            if (sortDefaultButton != null) sortDefaultButton.onClick.AddListener(() => _handVM.SortByDrawOrder());
            EnsureComponents();
            RefreshHand();
        }

        public override void Unbind()
        {
            _bindingGeneration++;
            if (_handVM != null) _handVM.CardAutomaticallyPlayed -= OnCardAutomaticallyPlayed;
            if (sortByCostButton != null) sortByCostButton.onClick.RemoveAllListeners();
            if (sortByColorButton != null) sortByColorButton.onClick.RemoveAllListeners();
            if (sortDefaultButton != null) sortDefaultButton.onClick.RemoveAllListeners();
            CancelPresentations();
            _resumePresentationComplete = null;
            _pool?.HideAll();
            _playability?.UpdatePlayability(_pool, null);
            _handVM = null;
            base.Unbind();
        }

        private void CancelPresentations()
        {
            _costFeedback?.Cancel();
            _generatedPresentation?.Cancel();
            _pendingGeneratedIds.Clear();
            _resumePresentationComplete += _drawPresentation?.Cancel();
            _resumePresentationComplete += _playSequencer?.CancelAll();
        }

        private void OnDisable() => CancelPresentations();

        private void OnEnable()
        {
            if (_handVM == null) return;
            RefreshHand();
            System.Action completion = _resumePresentationComplete;
            _resumePresentationComplete = null;
            int generation = _bindingGeneration;
            if (completion == null) return;
            foreach (System.Action callback in completion.GetInvocationList())
            {
                if (generation != _bindingGeneration || !isActiveAndEnabled) break;
                callback();
            }
        }

        public void ConfigureLayout(float angle, float radius, float scale,
            float spacing, float maxRot = 15f)
        {
            fanAngle = angle;
            fanRadius = radius;
            cardScale = scale;
            cardSpacing = spacing;
            maxRotation = maxRot;
        }

        private void EnsureComponents()
        {
            if (_pool != null) return;
            var profile = cardPresentation != null
                ? cardPresentation : CardPresentationProfile.LoadDefault();
            _pool = new CardViewPool(cardContainer, cardPrefab, profile);
            _playSequencer = new CardPlaySequencer(this, playPileView, cardContainer);
            _drawPresentation = new HandDrawPresentation(this, _playSequencer, profile);
            _costFeedback = new HandCostChangePresentation(this);
            _playability = new CardPlayabilityController(IsDrawPresentationReady);
            _playability.SetInteractionEnabled(
                _pool,
                _interactionEnabled);
            for (int i = 0; i < _pool.Count; i++)
            {
                CardView view = _pool.GetCardView(i);
                if (view == null) continue;
                CanvasGroup group = view.GetComponent<CanvasGroup>();
                if (group != null)
                    group.blocksRaycasts = _interactionEnabled && IsDrawPresentationReady(view.CardData.runtimeInstanceId);
            }
        }

        // ── 布局算法 ──────────────────────────────────────────

        private void RefreshHand()
        {
            if (_handVM == null || cardContainer == null) return;
            EnsureComponents();

            var hand = _handVM.SortedHand;
            int count = hand.Count;
            if (count == 0)
            {
                _costFeedback?.Cancel();
                _pool.HideAll();
                return;
            }

            _pool.EnsureCardViews(count);

            // 动态间距：卡牌越多间距越小
            float maxHandWidth = 1200f;
            float dynamicSpacing = cardSpacing;
            if (count > 1)
            {
                float needed = (count - 1) * cardSpacing;
                if (needed > maxHandWidth)
                    dynamicSpacing = maxHandWidth / (count - 1);
            }

            float totalWidth = (count - 1) * dynamicSpacing;
            float startX = -totalWidth * 0.5f;
            float arcHeight = Mathf.Clamp(fanRadius * 0.06f, 0f, 20f);

            for (int i = 0; i < count; i++)
            {
                var cardView = _pool[i];
                if (cardView == null) continue;
                int previousInstanceId = cardView.CardData.runtimeInstanceId;
                cardView.gameObject.SetActive(true);

                float x = startX + i * dynamicSpacing;
                float t = count > 1 ? (float)i / (count - 1) * 2f - 1f : 0f;
                float y = -t * t * arcHeight;
                float rotationScale = count > 1 ? Mathf.Min(1f, count / 7f) : 0f;
                float rotation = -t * maxRotation * rotationScale;

                var hover = cardView.GetComponent<CardHoverHandler>();
                if (hover != null)
                {
                    // A sorted slot may now represent another physical card.
                    // Old hover/return animation must not keep its transform.
                    if (cardView.CardData.runtimeInstanceId != hand[i].runtimeInstanceId)
                        hover.ResetForLayout();
                    hover.OriginalPos = new Vector2(x, y);
                    hover.OriginalRot = Quaternion.Euler(0, 0, rotation);
                    hover.OriginalScale = cardScale;
                    hover.OriginalSiblingIndex = i;
                    hover.CancelReturnForLayout();
                }

                bool isAnimating = hover != null && hover.IsAnimating;
                if (!isAnimating)
                {
                    // 根节点：只设置位置，不旋转不缩放（射线稳定）
                    cardView.transform.localPosition = new Vector3(x, y, 0);
                    cardView.transform.localRotation = Quaternion.identity;
                    cardView.transform.localScale = Vector3.one;
                    var rt = cardView.transform as RectTransform;
                    if (rt != null) rt.anchoredPosition = new Vector2(x, y);
                    // VisualRoot：旋转和缩放（视觉效果）
                    if (cardView.visualRoot != null)
                    {
                        cardView.visualRoot.localRotation = Quaternion.Euler(0, 0, rotation);
                        cardView.visualRoot.localScale = Vector3.one * cardScale;
                    }
                }

                cardView.UpdateCard(hand[i], i);
                _drawPresentation.CaptureLayout(cardView);
                SetCardArrivalVisibility(cardView, previousInstanceId != hand[i].runtimeInstanceId);
            }

            // 强制重排 sibling index
            for (int i = 0; i < count; i++)
            {
                var cardView = _pool[i];
                if (cardView == null) continue;
                var hover = cardView.GetComponent<CardHoverHandler>();
                if (hover == null || !hover.IsHovering)
                {
                    if (cardView.transform.parent == cardContainer)
                        cardView.transform.SetSiblingIndex(i);
                }
            }

            _pool.HideExtras(count);
            ApplyInteractionState();
            _costFeedback?.RefreshReadyCards(IsDrawPresentationReady);
        }

        /// <summary>
        /// 重排 cardContainer 内所有卡牌的 sibling index（hover 退出后调用）。
        /// </summary>
        public void ReorderSiblings()
        {
            if (_pool == null || cardContainer == null) return;
            for (int i = 0; i < _pool.Count; i++)
            {
                var cv = _pool[i];
                if (cv == null) continue;
                if (cv.transform.parent == cardContainer)
                    cv.transform.SetSiblingIndex(i);
            }
        }

        private void RefreshPileCounts()
        {
            if (_handVM == null) return;
            if (deckCountText != null)
                deckCountText.text = _handVM.DeckCount.ToString();
            if (discardCountText != null)
                discardCountText.text = _handVM.DiscardCount.ToString();
        }

        // ── 公共 API（委托给子组件）──────────────────────────

        /// <summary>Creates reusable slots without binding or drawing cards.</summary>
        public void PreparePool(int capacity)
        {
            EnsureComponents();
            _pool.EnsureCardViews(Mathf.Max(0, capacity));
            _pool.HideAll();
        }

        private System.Action<int> _cardClickCallback;
        private System.Action<int> _hoverEnterCallback;
        private System.Action _hoverExitCallback;

        public void SetCardClickCallback(System.Action<int> onCardClick)
        {
            _cardClickCallback = onCardClick;
            EnsureComponents();
            _pool.SetCallbacks(_cardClickCallback, _hoverEnterCallback, _hoverExitCallback);
        }

        public void SetHoverCallback(System.Action<int> onHoverEnter, System.Action onHoverExit)
        {
            _hoverEnterCallback = onHoverEnter;
            _hoverExitCallback = onHoverExit;
            EnsureComponents();
            _pool.SetCallbacks(_cardClickCallback, _hoverEnterCallback, _hoverExitCallback);
        }

        public CardView GetCardView(int sortedIndex)
        {
            EnsureComponents();
            return _pool.GetCardView(sortedIndex);
        }

        /// <summary>恢复所有手牌的基础数值（退出 hover 时调用）</summary>
        public void ResetAllComboPreviews()
        {
            EnsureComponents();
            int count = _pool.Count;
            for (int i = 0; i < count; i++)
            {
                var cv = _pool.GetCardView(i);
                if (cv != null) cv.ResetComboPreview();
            }
        }

        public void DetachCardView(CardView cardView)
        {
            EnsureComponents();
            _pool.RemoveCardView(cardView);
            _playSequencer.DetachForPlay(cardView);
        }

        public void ReattachCardView(CardView cardView, int index)
        {
            EnsureComponents();
            _pool.SetCardView(index, cardView);
            _playSequencer.ReattachForPlay(cardView);
            _handVM?.RefreshAll();
        }

        public void PlayCardAnimation(
            CardView cardView,
            bool destroyOnUse,
            System.Action onComplete)
        {
            EnsureComponents();
            _playSequencer.PlayCardAnimation(
                cardView,
                destroyOnUse,
                onComplete);
        }

        /// <summary>
        /// Queues authoritative drawn instances before the hand refresh. The
        /// next frame resolves their sorted CardViews and sends shared card
        /// backs to those exact destinations.
        /// </summary>
        public void QueueDrawPresentation(
            IReadOnlyList<int> cardInstanceIds,
            RectTransform drawSource,
            System.Action onComplete = null)
        {
            if (!isActiveAndEnabled)
            {
                _resumePresentationComplete += onComplete;
                return;
            }
            EnsureComponents();
            _drawPresentation.Queue(cardInstanceIds, drawSource, onComplete);
        }

        private void OnCardAutomaticallyPlayed(int instanceId, bool destroyOnUse)
            => _drawPresentation?.RecordAutomaticPlay(instanceId, destroyOnUse);

        internal void RefreshDrawPresentationVisibility()
        {
            for (int i = 0; _pool != null && i < _pool.Count; i++)
            {
                CardView view = _pool.GetCardView(i);
                if (view == null || !view.gameObject.activeSelf) continue;
                SetCardArrivalVisibility(view);
            }
            ApplyInteractionState();
        }

        internal void OnDrawCardArrived(int instanceId)
        {
            CardView view = FindInstanceView(instanceId);
            if (view != null) SetCardArrivalVisibility(view);
            ApplyInteractionState();
            _costFeedback?.RefreshReadyCards(IsDrawPresentationReady);
            CardDrawFeedbackRequested?.Invoke();
        }

        /// <summary>Queues settled cost changes until each real hand face has
        /// refreshed and its existing draw owner has delivered arrival.</summary>
        public void PresentHandCostChanges(IReadOnlyList<HandCardCostChange> changes)
        {
            if (!isActiveAndEnabled) return;
            EnsureComponents();
            _costFeedback.Queue(changes);
        }

        public void ClearHandCostFeedback() => _costFeedback?.Cancel();

        private void SetCardArrivalVisibility(CardView view, bool rebound = false)
        {
            CanvasGroup group = view.GetComponent<CanvasGroup>();
            if (group == null) return;
            int id = view.CardData.runtimeInstanceId;
            bool presenting = _drawPresentation?.IsPresenting(id) ?? false;
            if (_pendingGeneratedIds.Contains(id) || (_drawPresentation?.IsPending(id) ?? false) ||
                (rebound && presenting)) group.alpha = 0f;
            else if (!presenting) group.alpha = 1f;
            // A stable face in flight belongs to the sequencer's flip. Do not
            // hide another revealed face when one card arrives or the HUD refreshes.
        }

        private bool IsDrawPresentationReady(int instanceId) =>
            !(_drawPresentation?.IsAwaiting(instanceId) ?? false) &&
            !_pendingGeneratedIds.Contains(instanceId);

        /// <summary>Reserve real created instances before normal hand refresh;
        /// this does not consume a draw or remove a card from Core.</summary>
        public void QueueGeneratedCards(IReadOnlyList<int> instanceIds)
        {
            if (!isActiveAndEnabled) return;
            for (int i = 0; instanceIds != null && i < instanceIds.Count; i++)
                if (instanceIds[i] > 0) _pendingGeneratedIds.Add(instanceIds[i]);
        }

        public void PresentGeneratedCards(IReadOnlyList<int> instanceIds, Vector2 sourceScreen)
        {
            if (!isActiveAndEnabled) { RevealGeneratedCards(instanceIds); return; }
            if (_generatedPresentation == null)
                _generatedPresentation = GetComponent<GeneratedCardPresentation>() ??
                    gameObject.AddComponent<GeneratedCardPresentation>();
            _generatedPresentation.Play(instanceIds, sourceScreen, FindInstanceView, OnGeneratedCardArrived);
        }

        public void RevealGeneratedCards(IReadOnlyList<int> instanceIds)
        {
            for (int i = 0; instanceIds != null && i < instanceIds.Count; i++)
                OnGeneratedCardArrived(instanceIds[i]);
        }

        internal CardView FindInstanceView(int instanceId)
        {
            for (int i = 0; _pool != null && i < _pool.Count; i++)
            {
                CardView view = _pool.GetCardView(i);
                if (view != null && view.gameObject.activeSelf && view.CardData.runtimeInstanceId == instanceId)
                    return view;
            }
            return null;
        }

        private void OnGeneratedCardArrived(int instanceId)
        {
            if (!_pendingGeneratedIds.Remove(instanceId)) return;
            CardView view = FindInstanceView(instanceId);
            CanvasGroup group = view != null ? view.GetComponent<CanvasGroup>() : null;
            if (group != null && IsDrawPresentationReady(instanceId)) group.alpha = 1f;
            ApplyInteractionState();
            _costFeedback?.RefreshReadyCards(IsDrawPresentationReady);
        }

        public bool IsCardReadyForInteraction(int sortedIndex)
        {
            CardView view = _pool?.GetCardView(sortedIndex);
            CanvasGroup group = view != null ? view.GetComponent<CanvasGroup>() : null;
            return _interactionEnabled && view != null && view.gameObject.activeInHierarchy &&
                (group == null || group.alpha >= 0.999f) &&
                IsDrawPresentationReady(view.CardData.runtimeInstanceId);
        }

        public void UpdateCardPlayability(System.Func<int, bool> canPlayChecker)
        {
            EnsureComponents();
            _playability.UpdatePlayability(_pool, canPlayChecker);
        }

        /// <summary>
        /// 暂停或恢复整手牌交互。全局锁定不会复用费用不足的费用灰显；
        /// 它只结束悬停并关闭点击、射线与悬停反馈。
        /// </summary>
        public void SetInteractionEnabled(bool enabled)
        {
            if (_interactionEnabled != enabled)
            {
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[CombatHoverDiag] Hand interaction " +
                        $"{_interactionEnabled}->{enabled}; pool=" +
                        $"{(_pool != null ? _pool.Count : 0)}, " +
                        $"drawActive={_drawPresentation?.IsActive ?? false}, " +
                        $"generatedActive={_generatedPresentation?.IsActive ?? false}");
            }
            _interactionEnabled = enabled;
            if (_pool == null || _playability == null) return;
            ApplyInteractionState();
        }

        private void ApplyInteractionState()
        {
            if (_pool == null || _playability == null) return;
            _playability.SetInteractionEnabled(
                _pool,
                _interactionEnabled);
        }
    }
}
