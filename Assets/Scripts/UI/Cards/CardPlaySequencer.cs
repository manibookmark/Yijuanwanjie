using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 出牌动画流程：卡牌脱离手牌 → 飞向出牌堆 → 堆叠。
    /// 模块审查：只拥有卡牌运动、代理及演出计数，不拥有手牌身份或排序。
    /// 抽牌以不可变 ID 回报完成；布局换绑后停止写旧 View，由 HandView
    /// 恢复当前宿主。本次修复不增加职责；新增独立演出队列时重新评估拆分。
    /// 取消账本只管理这些动画自有的协程、代理和离手卡，不持有业务牌区；
    /// 停止时先失效旧代回调，再清理资源，不依赖协程 finally 一定被调用。
    /// </summary>
    public class CardPlaySequencer
    {
        private readonly MonoBehaviour _host;
        private readonly PlayPileView _playPileView;
        private readonly RectTransform _cardContainer;
        private readonly CardPresentationProfile _profile;
        private int _activePresentations;
        private int _generation;
        private readonly HashSet<RunningAnimation> _running = new HashSet<RunningAnimation>();
        private readonly HashSet<GameObject> _transients = new HashSet<GameObject>();
        private readonly HashSet<CardView> _departingCards = new HashSet<CardView>();
        private readonly Dictionary<object, System.Action> _completions =
            new Dictionary<object, System.Action>();

        private sealed class RunningAnimation { public Coroutine Handle; }

        // Cancellation does not depend on Unity disposing a stopped iterator.
        // Caller may resume completion notifications after restoring its binding.
        public System.Action CancelAll()
        {
            _generation++;
            System.Action completion = null;
            foreach (var callback in _completions.Values) completion += callback;
            _completions.Clear();
            var running = new List<RunningAnimation>(_running);
            _running.Clear();
            foreach (var animation in running)
                if (animation.Handle != null) _host.StopCoroutine(animation.Handle);
            foreach (GameObject transient in _transients)
                if (transient != null) Object.Destroy(transient);
            _transients.Clear();
            foreach (CardView card in _departingCards)
                if (card != null) Object.Destroy(card.gameObject);
            _departingCards.Clear();
            _activePresentations = 0;
            return completion;
        }

        private Coroutine StartOwned(IEnumerator routine)
        {
            var animation = new RunningAnimation();
            _running.Add(animation);
            animation.Handle = _host.StartCoroutine(RunOwned(routine, animation));
            return animation.Handle;
        }

        private IEnumerator RunOwned(IEnumerator routine, RunningAnimation animation)
        {
            try { yield return routine; }
            finally { _running.Remove(animation); }
        }

        public bool IsPresentationActive => _activePresentations > 0;
        public bool IsPaused { get; set; }

        private float PresentationUnscaledDeltaTime =>
            IsPaused ? 0f : Time.unscaledDeltaTime;

        public CardPlaySequencer(MonoBehaviour host,
            PlayPileView playPileView, RectTransform cardContainer,
            CardPresentationProfile profile = null)
        {
            _host = host;
            _playPileView = playPileView;
            _cardContainer = cardContainer;
            _profile = profile;
        }

        /// <summary>
        /// 从手牌中脱离卡牌：退出 hover、杀协程、移到 HoverLayer。
        /// </summary>
        public void DetachForPlay(CardView cardView)
        {
            if (cardView == null) return;
            var hover = cardView.GetComponent<CardHoverHandler>();
            if (hover != null)
            {
                hover.ExitHover();
                hover.StopAllCoroutines();
            }
            var rt = cardView.transform as RectTransform;
            if (rt == null) return;
            var hoverLayer = rt.parent?.parent?.Find("HoverLayer");
            if (hoverLayer != null)
                rt.SetParent(hoverLayer, false);
        }

        /// <summary>
        /// 出牌失败时把卡牌放回手牌容器。
        /// </summary>
        public void ReattachForPlay(CardView cardView)
        {
            if (cardView == null) return;
            var rt = cardView.transform as RectTransform;
            if (rt != null && rt.parent != _cardContainer)
                rt.SetParent(_cardContainer, false);
        }

        /// <summary>
        /// 出牌飞行动画：飞向出牌堆（或回退到屏幕中央消失）。
        /// </summary>
        public void PlayCardAnimation(
            CardView cardView,
            bool destroyOnUse,
            System.Action onComplete)
        {
            System.Action trackedComplete = BeginTrackedPresentation(() =>
            {
                _departingCards.Remove(cardView);
                onComplete?.Invoke();
            });
            if (cardView == null) { trackedComplete(); return; }
            _departingCards.Add(cardView);
            int pileGeneration = _playPileView != null
                ? _playPileView.CaptureGeneration()
                : 0;
            StartOwned(AnimatePlayCard(
                cardView,
                destroyOnUse,
                pileGeneration,
                trackedComplete));
        }

        private IEnumerator AnimatePlayCard(
            CardView cardView,
            bool destroyOnUse,
            int pileGeneration,
            System.Action onComplete)
        {
            try
            {
                if (cardView == null) yield break;
                var rt = cardView.transform as RectTransform;
                if (rt == null || rt.parent == null) yield break;

                Vector2 startPos = rt.anchoredPosition;
                Vector2 startScale = rt.localScale;
                var cg = cardView.GetComponent<CanvasGroup>();

                // Every played card follows the same flight path into the pile.
                // Destroy-on-use cards are removed immediately after landing so the
                // pile remains the single visual destination and source of truth.
                bool sendToPile = _playPileView != null;
                Vector2 targetPos;
                Vector3 targetScale;

                if (sendToPile)
                {
                    Vector3 localTarget =
                        rt.parent.InverseTransformPoint(_playPileView.transform.position);
                    targetPos = new Vector2(localTarget.x, localTarget.y);
                    targetScale = Vector3.one * _playPileView.PileCardScale;
                }
                else if (destroyOnUse)
                {
                    Vector3 localCenter = rt.parent.InverseTransformPoint(
                        _cardContainer.TransformPoint(_cardContainer.rect.center));
                    // Give destroy-on-use cards a clear lift before they dissolve.
                    // The hand sits low on the battle canvas, so the mathematical
                    // centre still reads as a low hover.  Raise the destination in
                    // the card parent by a visible, canvas-relative amount.
                    targetPos = new Vector2(localCenter.x, localCenter.y + 120f);
                    targetScale = startScale * 1.4f;
                }
                else
                {
                    targetPos = new Vector2(0f, 200f);
                    targetScale = startScale * 1.5f;
                }

                float duration = 0.35f;
                float t = 0f;
                while (t < duration)
                {
                    if (cardView == null || rt == null || rt.parent == null)
                        yield break;
                    t += Time.deltaTime;
                    if (sendToPile && _playPileView != null)
                    {
                        // Board projection may move during a window resize or camera motion.
                        Vector3 localTarget = rt.parent.InverseTransformPoint(
                            _playPileView.transform.position);
                        targetPos = new Vector2(localTarget.x, localTarget.y);
                        float parentScale = Mathf.Max(0.0001f, Mathf.Abs(rt.parent.lossyScale.x));
                        targetScale = Vector3.one * _playPileView.PileCardScale *
                            Mathf.Abs(_playPileView.transform.lossyScale.x) / parentScale;
                    }
                    float curve = Mathf.SmoothStep(0f, 1f, t / duration);
                    rt.anchoredPosition = Vector2.Lerp(startPos, targetPos, curve);
                    float s = Mathf.Lerp(startScale.x, targetScale.x, curve);
                    rt.localScale = new Vector3(s, s, 1f);
                    rt.localRotation = Quaternion.Lerp(
                        rt.localRotation, Quaternion.identity, curve * 0.5f);
                    yield return null;
                }

                if (cardView == null || rt == null) yield break;
                if (sendToPile && _playPileView != null)
                {
                    bool landed = _playPileView.TryAddCard(cardView, pileGeneration);
                    if (destroyOnUse && landed)
                    {
                        // Once landed, only this sequencer owns the dissolve.
                        // Shuffle/ClearPile must not destroy this temporary visual.
                        _playPileView.RemoveCard(cardView);
                        yield return FadeCardInPileRoutine(cardView, rt, cg);
                    }
                }
                else if (destroyOnUse)
                    yield return DestroyCardRoutine(cardView, rt, cg);
                else
                    Object.Destroy(cardView.gameObject);
            }
            finally
            {
                onComplete?.Invoke();
            }
        }

        /// <summary>
        /// Presents authoritative newly drawn card views. Each card remains
        /// hidden at its final hand slot until a shared text-free card back
        /// reaches that exact slot and flips into the real face.
        /// </summary>
        public void DrawCardsAnimation(
            IReadOnlyList<CardView> cardViews,
            RectTransform drawSource,
            System.Action<int> onCardArrived,
            System.Action onComplete)
        {
            System.Action trackedComplete = BeginTrackedPresentation(onComplete);
            if (cardViews == null || cardViews.Count == 0)
            {
                trackedComplete();
                return;
            }

            // Views are pooled by sorted slot; identity must be captured before
            // any delay, never read back from a possibly rebound view on arrival.
            var instanceIds = new int[cardViews.Count];
            for (int i = 0; i < cardViews.Count; i++)
            {
                instanceIds[i] = cardViews[i] != null
                    ? cardViews[i].CardData.runtimeInstanceId : 0;
                SetCardAlpha(cardViews[i], 0f);
            }
            int generation = _generation;
            StartOwned(AnimateDrawCards(
                cardViews,
                instanceIds,
                drawSource,
                id => { if (generation == _generation) onCardArrived?.Invoke(id); },
                trackedComplete));
        }

        private System.Action BeginTrackedPresentation(System.Action onComplete)
        {
            _activePresentations++;
            int generation = _generation;
            var token = new object();
            _completions.Add(token, onComplete);
            bool completed = false;
            return () =>
            {
                if (completed || generation != _generation) return;
                completed = true;
                _completions.Remove(token);
                _activePresentations = Mathf.Max(0, _activePresentations - 1);
                onComplete?.Invoke();
            };
        }

        private IEnumerator AnimateDrawCards(
            IReadOnlyList<CardView> cardViews,
            IReadOnlyList<int> instanceIds,
            RectTransform drawSource,
            System.Action<int> onCardArrived,
            System.Action onComplete)
        {
            float stagger = _profile != null
                ? _profile.drawCardStagger
                : 0.08f;
            float duration = _profile != null
                ? _profile.drawCardDuration
                : 0.44f;
            var animations = new List<Coroutine>(cardViews.Count);
            for (int i = 0; i < cardViews.Count; i++)
            {
                animations.Add(StartOwned(AnimateDrawCard(
                    cardViews[i],
                    instanceIds[i],
                    drawSource,
                    i * stagger,
                    duration,
                    onCardArrived)));
            }

            // Join the actual children instead of estimating their duration.
            foreach (Coroutine animation in animations)
                yield return animation;
            onComplete?.Invoke();
        }

        private IEnumerator AnimateDrawCard(
            CardView cardView,
            int instanceId,
            RectTransform drawSource,
            float delay,
            float duration,
            System.Action<int> onCardArrived)
        {
            RectTransform proxy = null;
            try
            {
                if (delay > 0f)
                    yield return WaitForPresentationSeconds(delay);
                if (!MatchesDrawInstance(cardView, instanceId))
                    yield break;

                RectTransform cardRect = cardView.transform as RectTransform;
                if (cardRect == null)
                {
                    SetCardAlpha(cardView, 1f);
                    yield break;
                }

                Vector2 start = ResolveAnchorPosition(drawSource);
                Vector2 destination = cardRect.parent == _cardContainer
                    ? cardRect.anchoredPosition
                    : ResolveAnchorPosition(cardRect);
                Vector2 control = (start + destination) * 0.5f +
                    Vector2.up * 130f;
                Vector2 cardSize = cardRect.rect.size;
                if (cardSize.x <= 1f || cardSize.y <= 1f)
                    cardSize = new Vector2(180f, 270f);

                proxy = CreateCardBackProxy(cardSize);
                _transients.Add(proxy.gameObject);
                proxy.anchoredPosition = start;
                proxy.localScale = Vector3.one * 0.24f;
                proxy.localRotation = Quaternion.Euler(0f, 0f, -12f);

                Transform visualRoot = cardView.visualRoot != null
                    ? cardView.visualRoot
                    : cardView.transform;
                Vector3 finalVisualScale = visualRoot.localScale;
                Quaternion finalVisualRotation = visualRoot.localRotation;
                CanvasGroup cardGroup = EnsureCanvasGroup(cardView);
                bool faceRevealed = false;
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    // A sort/play can reuse this slot while its proxy is flying.
                    // The hand owner will reveal the current view for the fixed ID.
                    if (!MatchesDrawInstance(cardView, instanceId))
                        yield break;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float motion = 1f - (1f - t) * (1f - t);
                    float inverse = 1f - motion;
                    Vector2 position =
                        inverse * inverse * start +
                        2f * inverse * motion * control +
                        motion * motion * destination;
                    proxy.anchoredPosition = position;

                    float baseScale = Mathf.Lerp(0.24f, 0.75f, motion);
                    float flip = Mathf.Clamp01((t - 0.76f) / 0.18f);
                    proxy.localScale = new Vector3(
                        baseScale * (1f - flip),
                        baseScale,
                        1f);
                    proxy.localRotation = Quaternion.Euler(
                        0f,
                        0f,
                        Mathf.Lerp(-12f, 0f, motion));

                    if (t >= 0.82f)
                    {
                        if (!faceRevealed)
                        {
                            faceRevealed = true;
                            cardGroup.alpha = 1f;
                        }
                        float reveal = Mathf.Clamp01((t - 0.82f) / 0.18f);
                        visualRoot.localScale = new Vector3(
                            finalVisualScale.x * reveal,
                            finalVisualScale.y,
                            finalVisualScale.z);
                        Image proxyImage = proxy.GetComponent<Image>();
                        if (proxyImage != null)
                        {
                            Color color = proxyImage.color;
                            color.a = 1f - reveal;
                            proxyImage.color = color;
                        }
                    }

                    elapsed += PresentationUnscaledDeltaTime;
                    yield return null;
                }

                if (!MatchesDrawInstance(cardView, instanceId))
                    yield break;
                cardGroup.alpha = 1f;
                visualRoot.localScale = finalVisualScale;
                visualRoot.localRotation = finalVisualRotation;
            }
            finally
            {
                if (proxy != null)
                {
                    _transients.Remove(proxy.gameObject);
                    Object.Destroy(proxy.gameObject);
                }
                onCardArrived?.Invoke(instanceId);
            }
        }

        private bool MatchesDrawInstance(CardView view, int instanceId) =>
            view != null && view.gameObject.activeInHierarchy &&
            view.transform.parent == _cardContainer &&
            view.CardData.runtimeInstanceId == instanceId;

        private IEnumerator DestroyCardRoutine(
            CardView cardView,
            RectTransform cardRect,
            CanvasGroup cardGroup)
        {
            cardGroup ??= EnsureCanvasGroup(cardView);
            // Let draw proxies land first when a card combines DrawCards and
            // destroyOnUse. The two rules remain independent but compose in a
            // readable order.
            yield return WaitForPresentationSeconds(0.12f);

            // Use many genuinely small sparks instead of a few large shards.
            // This keeps the dissolve airy while giving the burst enough
            // density to read at normal battle-canvas scale.
            const int auraParticleCount = 42;
            var auraParticles = new List<RectTransform>(auraParticleCount);
            var directions = new Vector2[auraParticleCount];
            Transform parent = cardRect.parent;
            Vector2 origin = cardRect.anchoredPosition;
            Color auraColor = cardView?.typeHeaderImage != null
                ? cardView.typeHeaderImage.color
                : new Color(1f, 0.78f, 0.32f, 1f);
            auraColor = Color.Lerp(auraColor, Color.white, 0.18f);
            for (int i = 0; i < auraParticleCount; i++)
            {
                var shardObject = new GameObject(
                    $"DestroyedCardAura_{i}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                _transients.Add(shardObject);
                shardObject.transform.SetParent(parent, false);
                Image auraImage = shardObject.GetComponent<Image>();
                auraImage.sprite = RoundedRectSpriteGenerator.GetFilled(2);
                auraImage.type = Image.Type.Sliced;
                auraImage.raycastTarget = false;
                auraImage.color = new Color(
                    auraColor.r, auraColor.g, auraColor.b,
                    0.98f - (i % 5) * 0.055f);
                RectTransform shardRect = auraImage.rectTransform;
                float size = 2.2f + (i % 4) * 0.9f;
                shardRect.sizeDelta = new Vector2(size, size * (0.9f + (i % 3) * 0.18f));
                float angle = (360f / auraParticleCount) * i +
                    (i % 2 == 0 ? -6f : 6f);
                directions[i] = new Vector2(
                    Mathf.Cos(angle * Mathf.Deg2Rad),
                    Mathf.Sin(angle * Mathf.Deg2Rad));
                shardRect.anchoredPosition = origin +
                    directions[i] * (7f + i % 3 * 3f);
                shardRect.localRotation = Quaternion.Euler(
                    0f,
                    0f, angle + 90f);
                auraParticles.Add(shardRect);
            }

            Vector3 baseScale = cardRect.localScale;
            Quaternion baseRotation = cardRect.localRotation;
            const float duration = 0.66f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                float curve = t * t * (3f - 2f * t);
                float cardFade = Mathf.Clamp01((t - 0.12f) / 0.34f);
                cardGroup.alpha = 1f - cardFade;
                cardRect.localScale = new Vector3(
                    baseScale.x * (1f + 0.16f * curve),
                    baseScale.y * (1f + 0.16f * curve),
                    baseScale.z);
                cardRect.anchoredPosition = origin + Vector2.up * (34f * curve);
                cardRect.localRotation = baseRotation;

                for (int i = 0; i < auraParticles.Count; i++)
                {
                    RectTransform shard = auraParticles[i];
                    if (shard == null) continue;
                    shard.anchoredPosition = origin +
                        directions[i] * (7f + 250f * curve) +
                        Vector2.up * (24f * curve + 66f * curve * curve);
                    shard.localRotation = Quaternion.Euler(
                        0f, 0f,
                        90f + i * 17f + 260f * curve);
                    Image image = shard.GetComponent<Image>();
                    if (image != null)
                    {
                        Color color = image.color;
                        color.a = (1f - Mathf.Clamp01((curve - 0.08f) / 0.92f)) * 0.98f;
                        image.color = color;
                    }
                }

                elapsed += PresentationUnscaledDeltaTime;
                yield return null;
            }

            for (int i = 0; i < auraParticles.Count; i++)
            {
                if (auraParticles[i] != null)
                {
                    _transients.Remove(auraParticles[i].gameObject);
                    Object.Destroy(auraParticles[i].gameObject);
                }
            }
            if (cardView != null)
                Object.Destroy(cardView.gameObject);
        }

        /// <summary>
        /// Destroy-on-use cards still land in the play pile, then dissolve in
        /// place. This preserves the shared pile flight while making the card
        /// leave a small, readable aura instead of lingering as a pile entry.
        /// </summary>
        private IEnumerator FadeCardInPileRoutine(
            CardView cardView,
            RectTransform cardRect,
            CanvasGroup cardGroup)
        {
            if (cardView == null || cardRect == null)
                yield break;

            cardGroup ??= EnsureCanvasGroup(cardView);
            Transform parent = cardRect.parent;
            if (parent == null)
            {
                Object.Destroy(cardView.gameObject);
                yield break;
            }

            const int particleCount = 18;
            var particles = new List<RectTransform>(particleCount);
            var directions = new Vector2[particleCount];
            Vector2 origin = cardRect.anchoredPosition;
            Color auraColor = cardView.typeHeaderImage != null
                ? cardView.typeHeaderImage.color
                : new Color(1f, 0.78f, 0.32f, 1f);
            auraColor = Color.Lerp(auraColor, Color.white, 0.28f);

            for (int i = 0; i < particleCount; i++)
            {
                var particleObject = new GameObject(
                    $"PlayedCardDissolve_{i}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                _transients.Add(particleObject);
                particleObject.transform.SetParent(parent, false);
                particleObject.transform.SetAsLastSibling();

                Image image = particleObject.GetComponent<Image>();
                image.sprite = RoundedRectSpriteGenerator.GetFilled(2);
                image.type = Image.Type.Sliced;
                image.raycastTarget = false;
                image.color = new Color(
                    auraColor.r, auraColor.g, auraColor.b,
                    0.82f - (i % 4) * 0.08f);

                RectTransform particle = image.rectTransform;
                float size = 1.4f + (i % 3) * 0.7f;
                particle.sizeDelta = new Vector2(size, size * 0.8f);
                float angle = (360f / particleCount) * i + (i % 2 == 0 ? -8f : 8f);
                directions[i] = new Vector2(
                    Mathf.Cos(angle * Mathf.Deg2Rad),
                    Mathf.Sin(angle * Mathf.Deg2Rad));
                particle.anchoredPosition = origin;
                particles.Add(particle);
            }

            Vector3 baseScale = cardRect.localScale;
            Quaternion baseRotation = cardRect.localRotation;
            const float duration = 0.26f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (cardView == null || cardRect == null)
                    break;
                float t = Mathf.Clamp01(elapsed / duration);
                float curve = t * t * (3f - 2f * t);
                float fade = Mathf.Clamp01((t - 0.05f) / 0.72f);
                if (cardGroup != null)
                    cardGroup.alpha = 1f - fade;
                float pulse = 1f + 0.08f * Mathf.Sin(t * Mathf.PI);
                cardRect.localScale = baseScale * pulse;
                cardRect.localRotation = baseRotation;

                for (int i = 0; i < particles.Count; i++)
                {
                    RectTransform particle = particles[i];
                    if (particle == null) continue;
                    particle.anchoredPosition = origin +
                        directions[i] * (4f + 42f * curve) +
                        Vector2.up * (8f * curve);
                    Image image = particle.GetComponent<Image>();
                    if (image != null)
                    {
                        Color color = image.color;
                        color.a *= 1f - curve;
                        image.color = color;
                    }
                }

                elapsed += PresentationUnscaledDeltaTime;
                yield return null;
            }

            for (int i = 0; i < particles.Count; i++)
            {
                if (particles[i] == null) continue;
                _transients.Remove(particles[i].gameObject);
                Object.Destroy(particles[i].gameObject);
            }
            if (cardView != null)
                Object.Destroy(cardView.gameObject);
        }

        private IEnumerator WaitForPresentationSeconds(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += PresentationUnscaledDeltaTime;
                yield return null;
            }
        }

        private RectTransform CreateCardBackProxy(Vector2 size)
        {
            var proxyObject = new GameObject(
                "DrawCardBack",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            proxyObject.transform.SetParent(_cardContainer, false);
            proxyObject.transform.SetAsLastSibling();
            Image background = proxyObject.GetComponent<Image>();
            background.sprite = RoundedRectSpriteGenerator.GetFilled(14);
            background.type = Image.Type.Sliced;
            background.color = new Color(0.86f, 0.78f, 0.6f, 1f);
            background.raycastTarget = false;
            RectTransform proxy = background.rectTransform;
            proxy.anchorMin = new Vector2(0.5f, 0.5f);
            proxy.anchorMax = new Vector2(0.5f, 0.5f);
            proxy.pivot = new Vector2(0.5f, 0.5f);
            proxy.sizeDelta = size;

            var borderObject = new GameObject(
                "Border",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            borderObject.transform.SetParent(proxy, false);
            Image border = borderObject.GetComponent<Image>();
            border.sprite = RoundedRectSpriteGenerator.GetOutline(14, 2);
            border.type = Image.Type.Sliced;
            border.color = new Color(0.2f, 0.14f, 0.08f, 0.9f);
            border.raycastTarget = false;
            Stretch(border.rectTransform, 7f);
            return proxy;
        }

        private Vector2 ResolveAnchorPosition(RectTransform anchor)
        {
            if (anchor == null)
                return new Vector2(-520f, 80f);
            if (anchor.parent == _cardContainer)
                return anchor.anchoredPosition;

            Canvas canvas = _cardContainer.GetComponentInParent<Canvas>();
            Camera camera = canvas != null &&
                canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(
                camera,
                anchor.position);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _cardContainer,
                    screenPoint,
                    camera,
                    out Vector2 localPoint))
                return localPoint;
            return new Vector2(-520f, 80f);
        }

        private static CanvasGroup EnsureCanvasGroup(CardView cardView)
        {
            if (cardView == null) return null;
            CanvasGroup group = cardView.GetComponent<CanvasGroup>();
            return group != null
                ? group
                : cardView.gameObject.AddComponent<CanvasGroup>();
        }

        private static void SetCardAlpha(CardView cardView, float alpha)
        {
            CanvasGroup group = EnsureCanvasGroup(cardView);
            if (group != null)
                group.alpha = alpha;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = Vector2.one * -inset;
        }
    }
}
