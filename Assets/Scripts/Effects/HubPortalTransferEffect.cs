using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns one uninterrupted visual journey from an activated ground formation
    /// into the stable realm-selection field. A single normalized progress drives
    /// both the depth streaks and the full-screen reading field; callers only ask
    /// to open or close and never coordinate particle, veil, or reveal timings.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class HubPortalTransferEffect : MonoBehaviour
    {
        public readonly struct PresentationSettings
        {
            public PresentationSettings(
                Vector2 formationPosition,
                Vector2 formationSize,
                float pullDuration,
                int pullParticleCount,
                float pullParticleScale,
                float pullParticleBrightness,
                float selectionScrimAlpha)
            {
                FormationPosition = formationPosition;
                FormationSize = formationSize;
                PullDuration = pullDuration;
                PullParticleCount = pullParticleCount;
                PullParticleScale = pullParticleScale;
                PullParticleBrightness = pullParticleBrightness;
                SelectionScrimAlpha = selectionScrimAlpha;
            }

            public Vector2 FormationPosition { get; }
            public Vector2 FormationSize { get; }
            public float PullDuration { get; }
            public int PullParticleCount { get; }
            public float PullParticleScale { get; }
            public float PullParticleBrightness { get; }
            public float SelectionScrimAlpha { get; }
        }

        private const float ContentRevealProgress = 0.68f;
        private static readonly Color ReadingFieldColour =
            new Color(0.004f, 0.018f, 0.024f, 1f);

        [Header("Journey")]
        [SerializeField, Range(0.55f, 1.1f)]
        private float _pullDuration = 0.76f;
        [SerializeField, Range(0.72f, 0.94f)]
        private float _selectionScrimAlpha = 0.88f;

        private CanvasGroup _canvasGroup;
        private Image _readingField;
        private HubPortalParticleScreen _particleScreen;
        private Coroutine _playRoutine;
        private float _journeyProgress;

        public bool IsPlaying => _playRoutine != null;
        public bool IsOpen { get; private set; }
        public event Action ScreenReady;
        public event Action ScreenClosed;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            BuildVisuals();
            ResetVisuals();
        }

        private void OnDisable()
        {
            StopPlayback();
            ResetVisuals();
        }

        /// <summary>
        /// Restarts the complete formation-to-realm journey. ScreenReady is raised
        /// once the reading field can support content, before the last streaks pass.
        /// </summary>
        public void Play()
        {
            StopPlayback();
            _playRoutine = StartCoroutine(OpenRoutine());
        }

        /// <summary>Dismisses the stable realm field and restores Hub idle.</summary>
        public void Close()
        {
            if (!IsOpen && _playRoutine == null)
                return;

            StopPlayback();
            _playRoutine = StartCoroutine(CloseRoutine());
        }

        public void Configure(PresentationSettings settings)
        {
            _pullDuration = Mathf.Clamp(settings.PullDuration, 0.55f, 1.1f);
            _selectionScrimAlpha = Mathf.Clamp(
                settings.SelectionScrimAlpha,
                0.72f,
                0.94f);

            if (_particleScreen != null)
            {
                _particleScreen.Configure(
                    settings.PullParticleCount,
                    settings.FormationPosition,
                    settings.FormationSize,
                    settings.PullParticleScale,
                    settings.PullParticleBrightness);
            }

            if (_readingField != null && _particleScreen != null)
                ApplyJourneyProgress(_journeyProgress);
        }

        private IEnumerator OpenRoutine()
        {
            ResetVisuals();
            _canvasGroup.alpha = 1f;
            _particleScreen.BeginOpening();

            bool contentRevealed = false;
            float elapsed = 0f;
            while (elapsed < _pullDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / _pullDuration);
                ApplyJourneyProgress(progress);
                if (!contentRevealed && progress >= ContentRevealProgress)
                {
                    contentRevealed = true;
                    IsOpen = true;
                    ScreenReady?.Invoke();
                }
                yield return null;
            }

            ApplyJourneyProgress(1f);
            if (!contentRevealed)
                ScreenReady?.Invoke();
            IsOpen = true;
            _playRoutine = null;
        }

        private IEnumerator CloseRoutine()
        {
            float startAlpha = _canvasGroup.alpha;
            const float duration = 0.18f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                _canvasGroup.alpha = Mathf.Lerp(
                    startAlpha,
                    0f,
                    progress * progress);
                yield return null;
            }

            ResetVisuals();
            _playRoutine = null;
            ScreenClosed?.Invoke();
        }

        private void ApplyJourneyProgress(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            _journeyProgress = clamped;
            _particleScreen.SetPullProgress(clamped);

            // The field arrives late enough to preserve the visible source at the
            // formation, then becomes stable before content finishes appearing.
            float fieldProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.34f, 0.88f, clamped));
            Color colour = ReadingFieldColour;
            colour.a = _selectionScrimAlpha * fieldProgress;
            _readingField.color = colour;
        }

        private void BuildVisuals()
        {
            var fieldObject = new GameObject(
                "RealmReadingField",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            fieldObject.transform.SetParent(transform, false);
            _readingField = fieldObject.GetComponent<Image>();
            _readingField.raycastTarget = false;
            Stretch(_readingField.rectTransform);

            var screenObject = new GameObject(
                "PortalDepthStreaks",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(CanvasGroup),
                typeof(HubPortalParticleScreen));
            screenObject.transform.SetParent(transform, false);
            RectTransform screenRect = screenObject.GetComponent<RectTransform>();
            Stretch(screenRect);
            _particleScreen = screenObject.GetComponent<HubPortalParticleScreen>();
        }

        private void ResetVisuals()
        {
            if (_canvasGroup == null)
                return;

            IsOpen = false;
            _journeyProgress = 0f;
            _canvasGroup.alpha = 0f;
            if (_readingField != null)
            {
                Color colour = ReadingFieldColour;
                colour.a = 0f;
                _readingField.color = colour;
            }
            if (_particleScreen != null)
                _particleScreen.ResetPresentation();
        }

        private void StopPlayback()
        {
            if (_playRoutine == null)
                return;
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
