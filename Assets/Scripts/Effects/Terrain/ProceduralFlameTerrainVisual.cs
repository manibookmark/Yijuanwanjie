using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Terrain
{
    /// <summary>
    /// Owns one anchored fire terrain's Infinity field, stack scale, short accents
    /// and removal. Native channels are configured before their single Play;
    /// subsequent pulses and fading change only transforms and owned materials.
    /// Terrain rules, target resolution and receipts remain with the presenter.
    /// </summary>
    public sealed class ProceduralFlameTerrainVisual : MonoBehaviour, ITerrainEffectVisual
    {
        public const string VisualTypeName = "ProceduralFlamePool";

        private readonly List<FireInfinityParticleField> _accents = new List<FireInfinityParticleField>();
        private TerrainVfxProfile _profile;
        private FireInfinityParticleField _baseFire;
        private Coroutine _pulse;
        private Coroutine _remove;
        private Vector3 _baseScale = Vector3.one;
        private int _stacks = 1;
        private float _baseOpacity = 0.72f;

        public void Initialize(TerrainVfxProfile profile, TerrainEffectChange initial)
        {
            _profile = profile;
            _stacks = Mathf.Max(1, initial.Stacks);
            _baseFire = FireInfinityParticleField.CreateTerrain(transform, profile, _stacks, persistent: true);
            ApplyStackIntensity(_stacks);
        }

        public void Place(TerrainEffectChange change)
        {
            if (_remove != null) return;
            _stacks = Mathf.Max(1, change.Stacks);
            ApplyStackIntensity(_stacks);
            AddAccent(trigger: false);
            if (_pulse != null) StopCoroutine(_pulse);
            _pulse = StartCoroutine(PulseRoutine(1.04f, 0.16f));
        }

        public void Trigger(TerrainEffectChange change)
        {
            if (_remove != null) return;
            _stacks = Mathf.Max(1, change.Stacks);
            ApplyStackIntensity(_stacks);
            AddAccent(trigger: true);
            if (_pulse != null) StopCoroutine(_pulse);
            _pulse = StartCoroutine(PulseRoutine(Mathf.Max(1f, _profile.triggerPulseScale), 0.20f));
        }

        public void BeginRemove(TerrainEffectChange change, Action completed)
        {
            if (_remove != null) return;
            if (_pulse != null)
            {
                StopCoroutine(_pulse);
                _pulse = null;
            }
            _remove = StartCoroutine(RemoveRoutine(completed));
        }

        private void AddAccent(bool trigger)
        {
            _accents.RemoveAll(field => field == null);
            _accents.Add(FireInfinityParticleField.CreateTerrain(transform, _profile, _stacks,
                persistent: false, trigger: trigger));
        }

        private void ApplyStackIntensity(int stacks)
        {
            float stackWidth = 1f + Mathf.Min(0.10f, (stacks - 1) * 0.02f);
            float stackHeight = 1f + Mathf.Min(0.12f, (stacks - 1) * 0.025f);
            _baseScale = new Vector3(stackWidth, stackHeight, 1f);
            if (_pulse == null) transform.localScale = _baseScale;
            // Stack updates do not touch a playing native emission module.
            float intensity = Mathf.Min(36f, _profile.baseEmission +
                _profile.emissionPerStack * Mathf.Max(0, stacks - 1));
            _baseOpacity = Mathf.Lerp(0.72f, 1f, Mathf.InverseLerp(_profile.baseEmission, 36f, intensity));
            if (_baseFire != null) _baseFire.SetOpacity(_baseOpacity);
        }

        private IEnumerator PulseRoutine(float scale, float duration)
        {
            float half = Mathf.Max(0.03f, duration * 0.5f);
            Vector3 peak = _baseScale * scale;
            float elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                transform.localScale = Vector3.Lerp(_baseScale, peak, Mathf.Clamp01(elapsed / half));
                yield return null;
            }
            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                transform.localScale = Vector3.Lerp(peak, _baseScale, Mathf.Clamp01(elapsed / half));
                yield return null;
            }
            transform.localScale = _baseScale;
            _pulse = null;
        }

        private IEnumerator RemoveRoutine(Action completed)
        {
            // Preserve the original receipt/removal schedule; managed opacity
            // replaces Stop on a native channel that has already started.
            yield return new WaitForSeconds(0.16f);
            float duration = Mathf.Max(0.05f, _profile.removeDuration);
            float elapsed = 0f;
            Vector3 startScale = transform.localScale;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.localScale = Vector3.Lerp(startScale, startScale * 0.72f, t);
                if (_baseFire != null) _baseFire.SetOpacity(_baseOpacity * (1f - t));
                foreach (FireInfinityParticleField accent in _accents)
                    if (accent != null) accent.SetOpacity(1f - t);
                yield return null;
            }
            completed?.Invoke();
            Destroy(gameObject);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (_baseFire != null) Destroy(_baseFire.gameObject);
            foreach (FireInfinityParticleField accent in _accents)
                if (accent != null) Destroy(accent.gameObject);
            _accents.Clear();
        }
    }
}
