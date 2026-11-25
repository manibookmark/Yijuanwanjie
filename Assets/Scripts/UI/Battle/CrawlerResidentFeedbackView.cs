using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>Owns both aura layers and their single phase/pulse clock. The
    /// sibling order is back aura, actor, front aura; neither graphic advances
    /// time. Gains and their numbers still belong to the HUD icons.</summary>
    [DisallowMultipleComponent]
    public sealed class CrawlerResidentFeedbackView : MonoBehaviour
    {
        private const float TriggerDuration = 0.72f;
        private RawImage _actor;
        private CrawlerResidentFootAuraGraphic _backAura;
        private CrawlerResidentFootAuraGraphic _frontAura;
        private float _phase;
        private float _cyclesPerSecond = 0.32f;
        private bool _previewVisible;
        private bool _tuningPreviewVisible;
        private bool _triggerActive;
        private float _triggerElapsed;
        public RectTransform EffectSource => transform as RectTransform;

        public void Initialize(RawImage actor)
        {
            if (actor == null) throw new System.ArgumentNullException(nameof(actor));
            if (_actor == actor && _backAura != null && _frontAura != null) return;
            if (_actor != null) throw new System.InvalidOperationException("A resident feedback view owns exactly one actor.");
            if (actor.transform.parent != transform)
                throw new System.ArgumentException("The resident actor must be a direct child of the feedback slot.", nameof(actor));
            _actor = actor;
            Outline legacyOutline = actor.GetComponent<Outline>();
            if (legacyOutline != null) { legacyOutline.effectColor = Color.clear; legacyOutline.enabled = false; }
            _backAura = CreateLayer("CrawlerTriggerSpiritBack");
            _frontAura = CreateLayer("CrawlerTriggerSpiritFront");
            _backAura.transform.SetAsFirstSibling();
            _frontAura.transform.SetSiblingIndex(actor.transform.GetSiblingIndex() + 1);
        }
        private CrawlerResidentFootAuraGraphic CreateLayer(string name)
        {
            var auraObject = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(CrawlerResidentFootAuraGraphic));
            auraObject.transform.SetParent(transform, false);
            RectTransform rect = auraObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return auraObject.GetComponent<CrawlerResidentFootAuraGraphic>();
        }
        public void ApplyAuraSettings(CrawlerResidentAuraSettings settings)
        {
            _cyclesPerSecond = Mathf.Clamp(settings.CyclesPerSecond,
                CrawlerResidentLayoutProfile.MinAuraCyclesPerSecond,
                CrawlerResidentLayoutProfile.MaxAuraCyclesPerSecond);
            _backAura?.ApplySettings(settings, false);
            _frontAura?.ApplySettings(settings, true);
        }
        public void ShowPreview(string effectText) { _previewVisible = true; ApplyVisual(); }
        public void HidePreview() { _previewVisible = false; ApplyVisual(); }
        public void SetTuningPreview(bool visible, string effectText) { _tuningPreviewVisible = visible; ApplyVisual(); }
        public void PlayTrigger(string effectText)
        { _triggerActive = true; _triggerElapsed = 0f; ApplyVisual(); }
        public void ResetFeedback()
        { _previewVisible = _tuningPreviewVisible = _triggerActive = false; _triggerElapsed = 0f; ApplyVisual(); }
        private void Update()
        {
            if (!_triggerActive && !_previewVisible && !_tuningPreviewVisible) return;
            _phase = Mathf.Repeat(_phase + Time.unscaledDeltaTime * _cyclesPerSecond, 1f);
            if (_triggerActive)
            {
                _triggerElapsed += Time.unscaledDeltaTime;
                if (_triggerElapsed >= TriggerDuration) _triggerActive = false;
            }
            ApplyVisual();
        }
        private void ApplyVisual()
        {
            if (_actor == null || _backAura == null || _frontAura == null) return;
            float alpha = Mathf.Clamp01(_actor.color.a);
            if ((!_triggerActive && !_previewVisible && !_tuningPreviewVisible) || alpha <= 0.001f)
            { SetIntensity(0f); return; }
            float intensity = 0.98f;
            if (_triggerActive)
            {
                float t = Mathf.Clamp01(_triggerElapsed / TriggerDuration);
                intensity = (1.08f + Mathf.Sin(t * Mathf.PI) * 0.27f) *
                    (1f - Mathf.Clamp01((t - 0.62f) / 0.38f));
            }
            SetIntensity(intensity * alpha);
        }
        private void SetIntensity(float intensity)
        {
            _backAura?.SetFrame(_phase, intensity);
            _frontAura?.SetFrame(_phase, intensity);
        }
        private void OnDisable() => ResetFeedback();
    }
}
