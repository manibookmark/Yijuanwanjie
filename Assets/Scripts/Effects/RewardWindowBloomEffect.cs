using System.Collections;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Effects.Map;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Plays one deterministic mirrored ruyi-cloud stroke around the reward
    /// window. Infinity is configured once and played once; after playback only
    /// ordinary Transforms move along the fixed Bezier path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RewardWindowBloomEffect : MonoBehaviour
    {
        public const int CurrentVisualLanguageVersion =
            RewardWindowBloomLanguage.RuntimeLanguageVersion;

        private const int PulseSortingOrder = 179;

        private RewardWindowBloomInfinityEmitter _emitter;
        private RewardWindowBloomGradeStyle _grade;
        private Coroutine _activation;
        private Coroutine _entranceSequence;
        private Coroutine _strokeSequence;
        private RewardRarity _rarity = RewardRarity.Common;
        private float _nextHoverTime;
        private bool _initialized;
        private bool _runtimeReady;
        private bool _entrancePending;
        private bool _entranceStarted;
        private bool _hoverPending;

        public RewardRarity Rarity => _rarity;
        public bool IsRuntimeReady => _runtimeReady;
        public int VisualLanguageVersion => CurrentVisualLanguageVersion;
        public int ParticleSystemCount =>
            _emitter != null ? _emitter.ParticleSystemCount : 0;

        public bool Initialize(RewardRarity rarity)
        {
            if (_initialized)
            {
                Debug.LogWarning(
                    "[RewardWindowBloom] 组件不能重复初始化。",
                    this);
                return _emitter != null && _emitter.IsActiveInHierarchy;
            }

            _rarity = NormalizeRarity(rarity);
            _grade = RewardWindowBloomLanguage.Resolve(_rarity);
            _entrancePending = true;
            _emitter = new RewardWindowBloomInfinityEmitter();
            uint seed = 81001u + (uint)((int)_rarity * 1009);
            if (!_emitter.Build(transform, gameObject.layer, seed))
            {
                _emitter = null;
                return false;
            }

            _initialized = true;
            ScheduleActivation();
            return true;
        }

        public void PlayEntrance()
        {
            if (_entranceStarted)
                return;
            _entrancePending = true;
            if (_runtimeReady)
                BeginEntrance();
            else
                ScheduleActivation();
        }

        public void PlayHoverPulse()
        {
            if (Time.unscaledTime < _nextHoverTime)
                return;
            _nextHoverTime = Time.unscaledTime + 0.62f;
            if (!_runtimeReady)
            {
                _hoverPending = true;
                ScheduleActivation();
                return;
            }

            PlayHoverGlow();
        }

        private void ScheduleActivation()
        {
            if (!_initialized || !isActiveAndEnabled ||
                _emitter == null || !_emitter.IsActiveInHierarchy ||
                _activation != null || _runtimeReady)
            {
                return;
            }
            _activation = StartCoroutine(ActivateAfterNativeReady());
        }

        private IEnumerator ActivateAfterNativeReady()
        {
            // Infinity 1.5.1 builds NativeModuleConfig in its own enable stack.
            // Keep every module access at least two stable frames away from it.
            yield return null;
            yield return null;
            if (!isActiveAndEnabled || _emitter == null ||
                !_emitter.IsActiveInHierarchy)
            {
                _activation = null;
                yield break;
            }

            if (!_emitter.ConfigureAfterNativeReady(_grade))
            {
                _activation = null;
                yield break;
            }

            // Configuration receives its own rebuild gate. No system has been
            // played yet, so automatic stroke emission cannot expire off camera.
            yield return null;
            yield return null;
            if (!isActiveAndEnabled || _emitter == null ||
                !_emitter.ReleaseAfterConfigurationGate())
            {
                _activation = null;
                yield break;
            }
            _runtimeReady = true;

            if (_entrancePending)
                BeginEntrance();
            if (_hoverPending)
            {
                _hoverPending = false;
                PlayHoverGlow();
            }
            _activation = null;
        }

        private void BeginEntrance()
        {
            if (_entranceStarted || _entranceSequence != null ||
                _emitter == null)
            {
                return;
            }

            _entrancePending = false;
            if (!_emitter.BeginStroke())
                return;

            _entranceStarted = true;
            _strokeSequence = StartCoroutine(PlayStrokeSequence());
            _entranceSequence = StartCoroutine(PlayEntranceGlowSequence());
        }

        private IEnumerator PlayStrokeSequence()
        {
            float elapsed = 0f;
            bool convergencePlayed = false;
            while (elapsed < _grade.StrokeDuration &&
                   isActiveAndEnabled && _emitter != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(
                    elapsed / _grade.StrokeDuration);
                // The twelve ordered segments retain a slight opening
                // acceleration and then slow visually as the two short tail
                // segments converge beneath the paper's top edge.
                float progress = normalized *
                                 (1.14f - 0.14f * normalized);
                _emitter.SetStrokeProgress(progress);
                if (!convergencePlayed &&
                    progress >=
                    RewardWindowBloomLanguage.ConvergenceGlowProgress)
                {
                    convergencePlayed = true;
                    PlayConvergencePearls();
                }
                yield return null;
            }

            if (_emitter != null)
            {
                if (!convergencePlayed)
                    PlayConvergencePearls();
                _emitter.SetStrokeProgress(1f);
                _emitter.EndStroke();
            }
            _strokeSequence = null;
        }

        private void PlayConvergencePearls()
        {
            RewardWindowBloomChannelStyle terminal =
                _grade.GetChannel(_grade.ChannelCount - 1);
            for (int sideIndex = 0;
                 sideIndex < RewardWindowBloomLanguage.SideCount;
                 sideIndex++)
            {
                StartCoroutine(AnimatePulse(
                    RewardWindowBloomLanguage.GetConvergenceAnchor(sideIndex),
                    _grade.ConvergencePearlDiameter,
                    _grade.ConvergencePearlDuration,
                    terminal.HighlightColor,
                    _grade.ConvergencePearlOpacity,
                    ManaSpiritCoreSprites.SoftOrb,
                    "SpiritRuyiConvergencePearl"));
            }

            if (_grade.Tier >= 4)
            {
                Vector2 left =
                    RewardWindowBloomLanguage.GetConvergenceAnchor(0);
                Vector2 right =
                    RewardWindowBloomLanguage.GetConvergenceAnchor(1);
                StartCoroutine(AnimatePulse(
                    (left + right) * 0.5f,
                    0.33f,
                    0.32f,
                    terminal.CoreColor,
                    0.10f,
                    ManaSpiritCoreSprites.SoftHalo,
                    "SpiritRuyiImmortalConvergenceHalo"));
            }
        }

        private IEnumerator PlayEntranceGlowSequence()
        {
            Color manaCore = SpiritEnergyColorPalette.ManaFull;
            Color manaHighlight = SpiritEnergyColorPalette.ManaHighlight;

            StartCoroutine(AnimatePulse(
                Vector2.zero,
                0.34f + _grade.Tier * 0.055f,
                0.22f + _grade.Tier * 0.015f,
                manaHighlight,
                0.20f + _grade.Tier * 0.03f,
                ManaSpiritCoreSprites.SoftOrb,
                "SpiritFireworkIgnition"));

            yield return new WaitForSecondsRealtime(0.045f);

            StartCoroutine(AnimatePulse(
                Vector2.zero,
                Mathf.Min(
                    2.14f + _grade.Tier * 0.08f,
                    RewardWindowBloomLanguage.StageHalfHeight * 1.62f),
                0.50f + _grade.Tier * 0.055f,
                manaCore,
                0.055f + _grade.Tier * 0.012f,
                ManaSpiritCoreSprites.SoftHalo,
                "SpiritFireworkAura"));

            yield return new WaitForSecondsRealtime(0.075f);

            StartCoroutine(AnimatePulse(
                Vector2.zero,
                0.56f + _grade.Tier * 0.07f,
                0.30f + _grade.Tier * 0.018f,
                manaHighlight,
                0.11f + _grade.Tier * 0.018f,
                ManaSpiritCoreSprites.SoftOrb,
                "SpiritFireworkEcho"));

            _entranceSequence = null;
        }

        private void PlayHoverGlow()
        {
            RewardWindowBloomChannelStyle channel = _grade.GetChannel(0);
            float diameter = 0.20f + _grade.Tier * 0.022f;
            float duration = 0.30f + _grade.Tier * 0.018f;
            for (int sideIndex = 0;
                 sideIndex < RewardWindowBloomLanguage.SideCount;
                 sideIndex++)
            {
                Vector2 anchor = RewardWindowBloomLanguage.GetHoverAnchor(
                    _grade,
                    sideIndex);
                StartCoroutine(AnimatePulse(
                    anchor,
                    diameter,
                    duration,
                    channel.CoreColor,
                    0.14f,
                    ManaSpiritCoreSprites.SoftOrb,
                    "SpiritFireworkHoverBreath"));
            }
        }

        private IEnumerator AnimatePulse(
            Vector2 anchor,
            float diameter,
            float duration,
            Color color,
            float peakAlpha,
            Sprite sprite,
            string objectName)
        {
            var pulseObject = new GameObject(
                objectName,
                typeof(SpriteRenderer));
            pulseObject.layer = gameObject.layer;
            pulseObject.transform.SetParent(transform, false);
            pulseObject.transform.localPosition =
                new Vector3(anchor.x, anchor.y, 0.02f);
            SpriteRenderer renderer =
                pulseObject.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = PulseSortingOrder;

            float elapsed = 0f;
            while (elapsed < duration && renderer != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float scale = Mathf.Lerp(
                    diameter * 0.24f,
                    diameter,
                    1f - Mathf.Pow(1f - normalized, 3f));
                pulseObject.transform.localScale = Vector3.one * scale;
                color.a = Mathf.Sin(normalized * Mathf.PI) * peakAlpha;
                renderer.color = color;
                yield return null;
            }

            if (pulseObject != null)
                Destroy(pulseObject);
        }

        private static RewardRarity NormalizeRarity(RewardRarity rarity)
        {
            return rarity switch
            {
                RewardRarity.Uncommon => rarity,
                RewardRarity.Rare => rarity,
                RewardRarity.VeryRare => rarity,
                RewardRarity.UltraRare => rarity,
                _ => RewardRarity.Common
            };
        }

        private void OnEnable()
        {
            if (_initialized)
                ScheduleActivation();
        }

        private void OnDisable()
        {
            if (_activation != null)
            {
                StopCoroutine(_activation);
                _activation = null;
            }
            if (_entranceSequence != null)
            {
                StopCoroutine(_entranceSequence);
                _entranceSequence = null;
            }
            if (_strokeSequence != null)
            {
                StopCoroutine(_strokeSequence);
                _strokeSequence = null;
            }

            // Infinity owns native disable cleanup. Never stop, clear, play or
            // write modules from this callback stack.
            _emitter?.EndStroke();
            _emitter?.InvalidateAfterDisable();
            _runtimeReady = false;
            _entrancePending = false;
            _entranceStarted = false;
            _hoverPending = false;
        }

        private void OnDestroy()
        {
            // Managed material disposal only. No Infinity module is read or
            // written while native state may be tearing down.
            _emitter?.DisposeManagedResources();
            _emitter = null;
        }
    }
}
