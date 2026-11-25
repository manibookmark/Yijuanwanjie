using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns the uninterrupted dormant-realm-to-awakened-realm visual timeline.
    /// Callers provide the formation screen-space anchor and receive facility
    /// progress snapshots; veil geometry, energy paths and timing stay hidden.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class HubRealmRestorationEffect : MonoBehaviour
    {
        public readonly struct FacilityFrame
        {
            public FacilityFrame(float portal, float tianlu)
            {
                PortalProgress = Mathf.Clamp01(portal);
                TianluProgress = Mathf.Clamp01(tianlu);
            }

            public float PortalProgress { get; }
            public float TianluProgress { get; }
            public float TianluGlow => Mathf.Sin(TianluProgress * Mathf.PI);
        }

        private CanvasGroup _canvasGroup;
        private HubRealmRestorationVeilGraphic _veil;
        private HubRealmRestorationEnergyGraphic _energy;
        private HubRealmRestorationProfile _profile;
        private Coroutine _playRoutine;
        private Action<FacilityFrame> _onFrame;
        private Action _onComplete;
        private Vector2 _portalTarget;
        private Vector2 _fallbackSpiritOrigin;

        public bool IsPlaying => _playRoutine != null;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            BuildVisuals();
            ShowCompleted();
        }

        public void Configure(HubRealmRestorationProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        /// <summary>
        /// Re-applies safe continuous settings without restarting the timeline
        /// or rebuilding particle topology. Used by Play-mode Inspector tuning.
        /// </summary>
        public void RefreshTuningPreview()
        {
            if (_profile == null || _veil == null || _energy == null)
                return;

            _veil.Configure(
                _profile.DormantVeilColor,
                _portalTarget,
                _profile.RealmRevealRadius,
                _profile.RealmRevealFeather);
            _energy.RefreshTuningPreview();
        }

        /// <summary>
        /// Freezes the realm in its dark, non-interactive pre-dialogue state.
        /// Particle topology is rebuilt here so Inspector count changes apply on
        /// the next replay while all continuous values remain live during play.
        /// </summary>
        public void PrepareDormant(Vector2 portalTarget)
        {
            EnsureConfigured();
            StopPlayback();
            _portalTarget = portalTarget;
            _fallbackSpiritOrigin = _profile.SpiritEnergyOrigin;
            _canvasGroup.alpha = 1f;
            gameObject.SetActive(true);
            _veil.Configure(
                _profile.DormantVeilColor,
                portalTarget,
                _profile.RealmRevealRadius,
                _profile.RealmRevealFeather);
            _veil.SetRevealProgress(0f);
            _energy.Configure(
                _profile,
                _profile.SpiritEnergyOrigin,
                Vector2.zero,
                portalTarget);
            _energy.SetProgress(0f);
        }

        /// <summary>
        /// Starts or restarts the complete awakening. Facility progress is a
        /// read-only semantic snapshot; the caller never coordinates sub-effects.
        /// </summary>
        public void Play(
            Action<FacilityFrame> onFrame,
            Action onComplete)
        {
            PlayFromCanvasSubject(
                _fallbackSpiritOrigin,
                Vector2.zero,
                onFrame,
                onComplete);
        }

        /// <summary>
        /// Starts the awakening from the character bounds captured at visual
        /// exit start. Screen conversion remains hidden inside this effect.
        /// </summary>
        public void PlayFromScreenSubject(
            Rect subjectScreenRect,
            Action<FacilityFrame> onFrame,
            Action onComplete)
        {
            RectTransform effectRect = transform as RectTransform;
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas != null &&
                            canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                effectRect,
                subjectScreenRect.center,
                camera,
                out Vector2 centre);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                effectRect,
                subjectScreenRect.min,
                camera,
                out Vector2 minimum);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                effectRect,
                subjectScreenRect.max,
                camera,
                out Vector2 maximum);
            PlayFromCanvasSubject(
                centre,
                Vector2.Max(Vector2.zero, maximum - minimum),
                onFrame,
                onComplete);
        }

        private void PlayFromCanvasSubject(
            Vector2 spiritOrigin,
            Vector2 spiritSourceSize,
            Action<FacilityFrame> onFrame,
            Action onComplete)
        {
            EnsureConfigured();
            StopPlayback();
            _energy.Configure(
                _profile,
                spiritOrigin,
                spiritSourceSize,
                _portalTarget);
            _onFrame = onFrame;
            _onComplete = onComplete;
            _playRoutine = StartCoroutine(PlayRoutine());
        }

        /// <summary>Stops without invoking completion and leaves normal Hub visible.</summary>
        public void ShowCompleted()
        {
            StopPlayback();
            if (_canvasGroup != null)
                _canvasGroup.alpha = 0f;
            _veil?.SetRevealProgress(1f);
            _energy?.SetProgress(0f);
        }

        private IEnumerator PlayRoutine()
        {
            float elapsed = 0f;
            while (elapsed < _profile.TotalDuration)
            {
                ApplyElapsed(elapsed);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            ApplyElapsed(_profile.TotalDuration);
            _canvasGroup.alpha = 0f;
            _playRoutine = null;
            Action callback = _onComplete;
            _onFrame = null;
            _onComplete = null;
            callback?.Invoke();
        }

        private void ApplyElapsed(float elapsed)
        {
            // Continuous profile reads intentionally make timing, size and
            // brightness edits visible while Play mode is paused or resumed.
            _veil.Configure(
                _profile.DormantVeilColor,
                _portalTarget,
                _profile.RealmRevealRadius,
                _profile.RealmRevealFeather);

            float spirit = StageProgress(
                elapsed,
                _profile.SpiritParticleStartDelay,
                _profile.SpiritFlightDuration);
            float realm = StageProgress(
                elapsed,
                _profile.RealmRevealDelay,
                _profile.RealmRevealDuration);
            float portal = StageProgress(
                elapsed,
                _profile.PortalRevealDelay,
                _profile.PortalRevealDuration);
            float tianlu = StageProgress(
                elapsed,
                _profile.TianluRevealDelay,
                _profile.TianluRevealDuration);
            _veil.SetRevealProgress(realm);
            _energy.SetProgress(spirit);
            _onFrame?.Invoke(new FacilityFrame(portal, tianlu));
        }

        private static float StageProgress(
            float elapsed, float delay, float duration)
        {
            return Mathf.Clamp01((elapsed - delay) / Mathf.Max(0.01f, duration));
        }

        private void BuildVisuals()
        {
            var veilObject = new GameObject(
                "DormantRealmVeil",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(HubRealmRestorationVeilGraphic));
            veilObject.transform.SetParent(transform, false);
            _veil = veilObject.GetComponent<HubRealmRestorationVeilGraphic>();
            Stretch(_veil.rectTransform);

            var energyObject = new GameObject(
                "RealmAwakeningEnergy",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(HubRealmRestorationEnergyGraphic));
            energyObject.transform.SetParent(transform, false);
            _energy = energyObject.GetComponent<HubRealmRestorationEnergyGraphic>();
            Stretch(_energy.rectTransform);
        }

        private void StopPlayback()
        {
            if (_playRoutine != null)
            {
                StopCoroutine(_playRoutine);
                _playRoutine = null;
            }
            _onFrame = null;
            _onComplete = null;
        }

        private void EnsureConfigured()
        {
            if (_profile == null)
                throw new InvalidOperationException("神域复苏特效尚未配置 Profile。");
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void OnDisable()
        {
            StopPlayback();
        }
    }
}
