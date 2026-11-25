using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 战斗中成果卡回到真实牌区的屏幕快照。只表达表现终点，不移动牌区实体。
    /// 未提供此快照时沿用地图的成果短暂停留与自动归卷。
    /// </summary>
    public readonly struct SealCardReturnTarget
    {
        public bool HasDestination { get; }
        public Vector2 ScreenPoint { get; }
        public float ScreenCardHeight { get; }
        public float RotationDegrees { get; }

        public SealCardReturnTarget(Vector2 screenPoint, float screenCardHeight,
            float rotationDegrees = 0f)
        {
            HasDestination = true;
            ScreenPoint = screenPoint;
            ScreenCardHeight = Mathf.Max(1f, screenCardHeight);
            RotationDegrees = rotationDegrees;
        }
    }

    /// <summary>
    /// 提交成功后钤印表现需要的 UI 快照。它只在 UI 程序集内部流动，
    /// Bootstrap 不接触卡牌、Transform、VideoPlayer 或 RenderTexture。
    /// </summary>
    internal sealed class SealStampVisualContext
    {
        public RectTransform Panel { get; }
        public RectTransform SelectedSlot { get; }
        public CardView SelectedCard { get; }
        public CanvasGroup[] CardGroups { get; }
        public int SelectedIndex { get; }
        public CardDisplayData CommittedDisplay { get; }
        public bool IsEvolution { get; }
        public SealCardReturnTarget? ReturnTarget { get; }

        public SealStampVisualContext(
            RectTransform panel,
            RectTransform selectedSlot,
            CardView selectedCard,
            IReadOnlyList<CanvasGroup> cardGroups,
            int selectedIndex,
            CardDisplayData committedDisplay,
            bool isEvolution = false,
            SealCardReturnTarget? returnTarget = null)
        {
            Panel = panel ?? throw new ArgumentNullException(nameof(panel));
            SelectedSlot = selectedSlot;
            SelectedCard = selectedCard;
            SelectedIndex = selectedIndex;
            CommittedDisplay = committedDisplay;
            IsEvolution = isEvolution;
            ReturnTarget = returnTarget;
            CardGroups = CopyGroups(cardGroups);
        }

        private static CanvasGroup[] CopyGroups(
            IReadOnlyList<CanvasGroup> groups)
        {
            if (groups == null)
                return Array.Empty<CanvasGroup>();
            var copy = new CanvasGroup[groups.Count];
            for (int index = 0; index < groups.Count; index++)
                copy[index] = groups[index];
            return copy;
        }
    }

    /// <summary>
    /// 唯一拥有钤印提交后的表现状态：两秒键色视频与命中、
    /// 成果卡稳定停留和归卷。地图短暂停留后自动化粒收牌，战斗由真实牌区
    /// 屏幕快照自动归库。规则提交和音频播放由外部协调。
    /// 规模审查：本文件超过 500 行，但视频预热、命中、成果停留和归卷共享
    /// 同一批 VideoPlayer、RenderTexture、卡牌 Transform 与 exactly-once 完成不变量，
    /// 因此保留为一个内聚 VFX 状态机。进化聚能、爆发和碎光已独立为
    /// EvolutionCardPresentation，它只拥有自己的网格与时钟，不写卡牌状态；
    /// 本类仅在提交演出的类型化阶段发令。新增另一类卡牌变换需重新审查。
    /// </summary>
    public sealed class SealStampPresentation : MonoBehaviour
    {
        internal const float VideoDuration = 2f;
        internal const float ImpactTime = 1.25f;
        internal const float ResultHoldDuration = 0.70f;
        internal const float CollectionDuration = 0.82f;

        private const float FocusEnd = 0.20f;
        private const float VideoFadeDuration = 0.14f;
        private const float PrepareTimeout = 0.75f;
        private const string VideoResourcePath =
            "Video/Effects/yuyin-stamping-screen-impact-2s";
        private const string ShaderResourcePath =
            "Shaders/SealStampChromaKey";
        private const string ParticleShaderResourcePath =
            "Shaders/SealStampParticle";

        private Coroutine _routine;
        private VideoPlayer _videoPlayer;
        private RawImage _videoView;
        private RenderTexture _renderTexture;
        private Material _keyMaterial;
        private Material _particleMaterial;
        private SealStampBurstGraphic _burstGraphic;
        private EvolutionCardPresentation _evolutionPresentation;
        private SealStampVfxProfile _vfxProfile;
        private Action _onPlaybackStarted;
        private Action _onResultShown;
        private Action _onCollectionStarted;
        private Action _onComplete;
        private Action _onInterrupted;
        private bool _prepared;
        private bool _videoFailed;
        private bool _playbackStarted;
        private bool _resultShown;
        private bool _finished = true;

        /// <summary>
        /// 在获得态或选卡态预解码视频首帧，提交后不再为导入或解码停顿。
        /// 素材缺失时保留卡牌提交动画作为降级路径。
        /// </summary>
        internal void Prepare(RectTransform panel)
        {
            if (panel == null || _videoPlayer != null || _videoFailed)
                return;

            EnsureBurstGraphic(panel);

            VideoClip clip = Resources.Load<VideoClip>(VideoResourcePath);
            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (clip == null || shader == null)
            {
                _videoFailed = true;
                Debug.LogWarning(
                    "[SealStampPresentation] 钤印视频或键色 Shader 缺失，" +
                    "将使用无视频降级表现。");
                return;
            }

            var viewObject = new GameObject(
                "SealStampVideo",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage),
                typeof(AspectRatioFitter));
            viewObject.transform.SetParent(panel, false);
            _videoView = viewObject.GetComponent<RawImage>();
            _videoView.raycastTarget = false;
            _videoView.enabled = false;
            RectTransform viewRect = _videoView.rectTransform;
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero;
            viewRect.offsetMax = Vector2.zero;
            AspectRatioFitter aspect = viewObject.GetComponent<
                AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 16f / 9f;

            _renderTexture = new RenderTexture(
                1280,
                720,
                0,
                RenderTextureFormat.ARGB32)
            {
                name = "SealStampRenderTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _renderTexture.Create();
            _keyMaterial = new Material(shader)
            {
                name = "SealStampChromaKeyRuntime"
            };
            _videoView.texture = _renderTexture;
            _videoView.material = _keyMaterial;

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = false;
            _videoPlayer.skipOnDrop = false;
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.targetTexture = _renderTexture;
            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = clip;
            _videoPlayer.prepareCompleted += OnPrepared;
            _videoPlayer.errorReceived += OnVideoError;
            _prepared = _videoPlayer.isPrepared;
            if (!_prepared)
                _videoPlayer.Prepare();
        }

        internal void Play(
            SealStampVisualContext context,
            Action onPlaybackStarted,
            Action onResultShown,
            Action onCollectionStarted,
            Action onComplete,
            Action onInterrupted = null)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            CancelSilently();
            _onPlaybackStarted = onPlaybackStarted;
            _onResultShown = onResultShown;
            _onCollectionStarted = onCollectionStarted;
            _onComplete = onComplete;
            _onInterrupted = onInterrupted;
            _playbackStarted = false;
            _resultShown = false;
            _finished = false;
            Prepare(context.Panel);
            _routine = StartCoroutine(Run(context));
        }

        private IEnumerator Run(SealStampVisualContext context)
        {
            float prepareElapsed = 0f;
            while (!_prepared && !_videoFailed &&
                   prepareElapsed < PrepareTimeout)
            {
                prepareElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Vector3 centre = context.Panel.TransformPoint(
                context.Panel.rect.center);
            Vector3 cardStart = context.SelectedSlot != null
                ? context.SelectedSlot.position
                : centre;
            if (context.SelectedSlot != null)
            {
                context.SelectedSlot.SetParent(context.Panel, true);
                context.SelectedSlot.SetAsLastSibling();
            }

            if (context.IsEvolution)
            {
                _evolutionPresentation ??= EvolutionCardPresentation.Create(context.Panel);
                _evolutionPresentation.BeginCharge(context.SelectedSlot, VideoDuration);
            }

            StartVideoIfAvailable();
            InvokePlaybackStartedOnce();

            float elapsed = 0f;
            bool impactPlayed = false;
            bool committedShown = false;
            float resultReadyTime = VideoDuration + (context.IsEvolution
                ? EvolutionCardPresentation.ResultHoldDuration
                : ResultHoldDuration);
            while (elapsed < resultReadyTime)
            {
                ApplyPreCollectionFrame(
                    context,
                    cardStart,
                    centre,
                    elapsed,
                    ref impactPlayed,
                    ref committedShown);
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }

            ApplyPreCollectionFrame(
                context,
                cardStart,
                centre,
                resultReadyTime,
                ref impactPlayed,
                ref committedShown);
            Action collectionStarted = _onCollectionStarted;
            _onCollectionStarted = null;
            collectionStarted?.Invoke();
            if (context.ReturnTarget.HasValue)
                yield return ReturnCommittedCard(context, context.ReturnTarget.Value);
            else
                yield return CollectCommittedCard(context, centre);
            FinishOnce();
        }

        private IEnumerator ReturnCommittedCard(
            SealStampVisualContext context, SealCardReturnTarget target)
        {
            RectTransform card = context.SelectedSlot;
            if (card == null) yield break;
            _burstGraphic?.StopBurst();
            Canvas canvas = context.Panel.GetComponentInParent<Canvas>();
            Camera camera = canvas != null &&
                canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector3 start = card.position;
            Vector3 destination = start;
            if (target.HasDestination)
            {
                if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                        context.Panel, target.ScreenPoint, camera, out destination))
                    destination = start;
            }
            RectTransform cardRect = context.SelectedCard != null
                ? (context.SelectedCard.visualRoot != null ? context.SelectedCard.visualRoot :
                    context.SelectedCard.transform as RectTransform) : null;
            float screenHeight = cardRect != null ? Vector2.Distance(
                RectTransformUtility.WorldToScreenPoint(camera,
                    cardRect.TransformPoint(new Vector3(0f, cardRect.rect.yMin, 0f))),
                RectTransformUtility.WorldToScreenPoint(camera,
                    cardRect.TransformPoint(new Vector3(0f, cardRect.rect.yMax, 0f)))) : 300f;
            float endScale = target.HasDestination
                ? Mathf.Clamp(target.ScreenCardHeight / Mathf.Max(1f, screenHeight), 0.05f, 2f)
                : 0.9f;
            CanvasGroup group = context.SelectedIndex >= 0 &&
                context.SelectedIndex < context.CardGroups.Length
                ? context.CardGroups[context.SelectedIndex] : null;
            Vector3 startScale = card.localScale;
            Quaternion startRotation = card.rotation;
            Quaternion endRotation = Quaternion.Euler(0f, 0f, target.RotationDegrees);
            const float duration = 0.52f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                card.position = Vector3.Lerp(start, destination, eased);
                card.localScale = startScale * Mathf.Lerp(1f, endScale, eased);
                card.rotation = Quaternion.Slerp(startRotation, endRotation, eased);
                if (group != null)
                    group.alpha = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(target.HasDestination ? 0.8f : 0f, 1f, progress));
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            card.position = destination;
            card.localScale = startScale * endScale;
            card.rotation = endRotation;
            if (group != null) group.alpha = 0f;
        }

        private void ApplyPreCollectionFrame(
            SealStampVisualContext context,
            Vector3 cardStart,
            Vector3 centre,
            float elapsed,
            ref bool impactPlayed,
            ref bool committedShown)
        {
            float focus = Mathf.Clamp01(elapsed / FocusEnd);
            float focusEased = 1f - Mathf.Pow(1f - focus, 3f);
            if (context.SelectedSlot != null)
            {
                context.SelectedSlot.position = Vector3.Lerp(
                    cardStart,
                    centre,
                    focusEased);
            }
            FadeOtherCards(
                context.CardGroups,
                context.SelectedIndex,
                focusEased);

            if (!impactPlayed && elapsed >= ImpactTime)
            {
                impactPlayed = true;
                _burstGraphic?.PlayBurst();
            }

            // 朱砂印只在视频完整结束后落到成果卡上；命中过程仍展示
            // 未钤印卡面，避免玩家提前看到结果。
            if (!committedShown && elapsed >= VideoDuration)
            {
                committedShown = true;
                if (context.SelectedCard != null)
                {
                    context.SelectedCard.UpdateCard(
                        context.CommittedDisplay,
                        context.SelectedIndex);
                    context.SelectedCard.SetSealAssignmentPreview(
                        false,
                        -1,
                        null);
                }
                if (context.IsEvolution)
                    _evolutionPresentation?.Reveal();
                InvokeResultShownOnce();
            }

            if (context.SelectedSlot != null &&
                elapsed >= ImpactTime && elapsed < VideoDuration)
                UpdateCardImpact(context.SelectedSlot, centre, elapsed);
            else if (context.SelectedSlot != null && committedShown)
                HoldCommittedCard(
                    context.SelectedSlot,
                    centre,
                    elapsed - VideoDuration);

            UpdateVideoVisibility(elapsed);
        }

        private void StartVideoIfAvailable()
        {
            if (!_prepared || _videoFailed || _videoPlayer == null ||
                _videoView == null)
                return;

            _videoView.enabled = true;
            _videoView.color = Color.white;
            _videoView.rectTransform.SetAsLastSibling();
            _videoPlayer.Stop();
            _videoPlayer.frame = 0;
            _videoPlayer.time = 0d;
            _videoPlayer.Play();
        }

        internal bool IsPresentationVisual(GameObject candidate)
        {
            if (candidate == null)
                return false;
            return (_videoView != null &&
                    candidate == _videoView.gameObject) ||
                   (_burstGraphic != null &&
                    candidate == _burstGraphic.gameObject) ||
                   (_evolutionPresentation != null &&
                    candidate == _evolutionPresentation.gameObject);
        }

        private void InvokePlaybackStartedOnce()
        {
            if (_playbackStarted)
                return;
            _playbackStarted = true;
            Action callback = _onPlaybackStarted;
            _onPlaybackStarted = null;
            callback?.Invoke();
        }

        private void InvokeResultShownOnce()
        {
            if (_resultShown)
                return;
            _resultShown = true;
            Action callback = _onResultShown;
            _onResultShown = null;
            callback?.Invoke();
        }

        private void UpdateVideoVisibility(float elapsed)
        {
            if (_videoView == null || !_videoView.enabled)
                return;
            if (elapsed >= VideoDuration)
            {
                if (_videoPlayer != null)
                    _videoPlayer.Stop();
                _videoView.enabled = false;
                return;
            }

            float fadeStart = VideoDuration - VideoFadeDuration;
            float alpha = elapsed <= fadeStart
                ? 1f
                : 1f - Mathf.InverseLerp(fadeStart, VideoDuration, elapsed);
            Color color = _videoView.color;
            color.a = alpha;
            _videoView.color = color;
        }

        private static void UpdateCardImpact(
            RectTransform card,
            Vector3 centre,
            float elapsed)
        {
            float impact = Mathf.InverseLerp(
                ImpactTime,
                ImpactTime + 0.20f,
                elapsed);
            Vector2 squash = impact < 0.45f
                ? Vector2.Lerp(
                    new Vector2(1.08f, 0.92f),
                    new Vector2(0.98f, 1.02f),
                    impact / 0.45f)
                : Vector2.Lerp(
                    new Vector2(0.98f, 1.02f),
                    Vector2.one,
                    (impact - 0.45f) / 0.55f);
            card.position = centre;
            card.localScale = new Vector3(squash.x, squash.y, 1f);
            card.localRotation = Quaternion.identity;
        }

        private static void HoldCommittedCard(
            RectTransform card,
            Vector3 centre,
            float elapsed)
        {
            if (card == null)
                return;
            float lift = 5f + Mathf.Sin(elapsed * 2.4f) * 3f;
            Vector3 position = centre + Vector3.up * lift;
            position.x = Mathf.Round(position.x);
            position.y = Mathf.Round(position.y);
            card.position = position;
            card.localScale = Vector3.one;
            card.localRotation = Quaternion.identity;
        }

        private IEnumerator CollectCommittedCard(
            SealStampVisualContext context,
            Vector3 centre)
        {
            RectTransform card = context.SelectedSlot;
            if (card == null)
                yield break;

            CanvasGroup group = context.SelectedIndex >= 0 &&
                                context.SelectedIndex <
                                context.CardGroups.Length
                ? context.CardGroups[context.SelectedIndex]
                : null;
            _burstGraphic?.PlayDissolve(card);
            float duration = _burstGraphic != null
                ? _burstGraphic.DissolveDuration
                : CollectionDuration;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                card.position = centre + new Vector3(
                    Mathf.Lerp(0f, 12f, eased),
                    Mathf.Lerp(5f, 22f, eased),
                    0f);
                card.localScale = Vector3.one * Mathf.Lerp(
                    1f, 0.96f, eased);
                card.localRotation = Quaternion.identity;
                if (group != null)
                {
                    float fade = Mathf.InverseLerp(0.12f, 1f, progress);
                    group.alpha = 1f - fade * fade * (3f - 2f * fade);
                }
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            card.position = centre + new Vector3(12f, 22f, 0f);
            card.localScale = Vector3.one * 0.96f;
            card.localRotation = Quaternion.identity;
            if (group != null)
                group.alpha = 0f;
        }

        private static void FadeOtherCards(
            CanvasGroup[] groups,
            int selectedIndex,
            float progress)
        {
            for (int index = 0; index < groups.Length; index++)
            {
                if (index == selectedIndex || groups[index] == null)
                    continue;
                groups[index].alpha = Mathf.Lerp(0.45f, 0f, progress);
            }
        }

        private void OnPrepared(VideoPlayer source)
        {
            if (source == _videoPlayer)
                _prepared = true;
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            if (source != _videoPlayer)
                return;
            _videoFailed = true;
            _prepared = false;
            if (_videoView != null)
                _videoView.enabled = false;
            Debug.LogWarning(
                $"[SealStampPresentation] 视频播放失败，使用降级表现：{message}");
        }

        private void CancelActive()
        {
            if (_routine != null)
                StopCoroutine(_routine);
            _routine = null;
            if (!_finished)
                FinishOnce(interrupted: true);
        }

        internal void CancelSilently()
        {
            _onPlaybackStarted = null;
            _onResultShown = null;
            _onCollectionStarted = null;
            _onComplete = null;
            _onInterrupted = null;
            CancelActive();
        }

        private void FinishOnce(bool interrupted = false)
        {
            if (_finished)
                return;
            _finished = true;
            _routine = null;
            if (_videoPlayer != null)
                _videoPlayer.Stop();
            if (_videoView != null)
            {
                _videoView.enabled = false;
                _videoView.color = Color.white;
            }
            _burstGraphic?.StopBurst();
            _evolutionPresentation?.StopPresentation();
            _onPlaybackStarted = null;
            _onResultShown = null;
            _onCollectionStarted = null;
            Action callback = interrupted ? _onInterrupted : _onComplete;
            _onComplete = null;
            _onInterrupted = null;
            callback?.Invoke();
        }

        private void OnDisable()
        {
            CancelActive();
        }

        private void OnDestroy()
        {
            CancelActive();
            if (_videoPlayer != null)
            {
                _videoPlayer.prepareCompleted -= OnPrepared;
                _videoPlayer.errorReceived -= OnVideoError;
            }
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
            if (_keyMaterial != null)
            {
                Destroy(_keyMaterial);
                _keyMaterial = null;
            }
            if (_particleMaterial != null)
            {
                Destroy(_particleMaterial);
                _particleMaterial = null;
            }
        }

        private void EnsureBurstGraphic(RectTransform panel)
        {
            _vfxProfile ??= SealStampVfxProfile.LoadDefault();
            if (_burstGraphic != null)
            {
                _burstGraphic.Configure(_vfxProfile);
                return;
            }
            var burstObject = new GameObject(
                "SealStampProgrammaticBurst",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(SealStampBurstGraphic));
            burstObject.transform.SetParent(panel, false);
            RectTransform rect = burstObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _burstGraphic = burstObject.GetComponent<
                SealStampBurstGraphic>();
            _burstGraphic.raycastTarget = false;
            _burstGraphic.Configure(_vfxProfile);
            Shader particleShader = Resources.Load<Shader>(
                ParticleShaderResourcePath);
            if (particleShader != null)
            {
                _particleMaterial = new Material(particleShader)
                {
                    name = "SealStampParticleMaterialRuntime"
                };
                _burstGraphic.material = _particleMaterial;
            }
            _burstGraphic.StopBurst();
        }
    }
}
