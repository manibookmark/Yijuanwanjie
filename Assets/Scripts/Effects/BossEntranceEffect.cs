using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 在独立运动根上呈现一个已经构建完成的关 BOSS，不拥有战斗或音频状态。
    /// 编队根永远不由本模块写入；Prepare 同步隐藏主体，避免最终姿态闪现一帧。
    ///
    /// 落地粒子的原生生命周期由 BossInfinityParticleField 单独拥有。
    /// 模块审查：本类仍只拥有登场时钟与姿态恢复；水流的几何、材质与清理由
    /// BossWaterConvergenceEffect 隔离，不在此扩展第二套粒子状态机。
    /// </summary>
    public sealed class BossEntranceEffect : MonoBehaviour
    {
        private SpriteRenderer _subject;
        private Transform _motionRoot;
        private Camera _presentationCamera;
        private BossEntranceEffectPreset _preset;
        private BossEntranceEffectMode _mode;
        private Vector3 _originalMotionLocalPosition;
        private Vector3 _originalMotionLocalScale;
        private Quaternion _originalMotionLocalRotation;
        private Color _originalColor;
        private bool _originalRendererEnabled;
        private readonly List<Renderer> _companionRenderers =
            new List<Renderer>();
        private readonly List<bool> _companionRendererStates =
            new List<bool>();
        private BossInfinityParticleWarmup _particleWarmup;
        private BossInfinityParticleField _landingParticles;
        private BossWaterConvergenceEffect _waterEffect;
        private Coroutine _routine;
        private Action _onLanding;
        private Action _onComplete;
        private bool _restored;
        private bool _landingSignaled;

        public bool IsPrepared { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsComplete { get; private set; }
        public int LandingCount { get; private set; }
        public SpriteRenderer Subject => _subject;
        public BossInfinityParticleField LandingParticles =>
            _landingParticles;
        public BossEntranceEffectPreset Preset => _preset;
        public BossEntranceEffectMode Mode => _mode;

        public static BossEntranceEffect Prepare(
            SpriteRenderer subject,
            Transform motionRoot,
            Transform effectParent,
            Camera presentationCamera = null,
            BossEntranceEffectPreset preset = null,
            BossEntranceEffectMode mode = BossEntranceEffectMode.ImpactDrop)
        {
            return PrepareCore(
                subject,
                motionRoot,
                effectParent,
                presentationCamera,
                preset,
                mode,
                null);
        }

        internal static BossEntranceEffect PrepareWithWarmup(
            SpriteRenderer subject,
            Transform motionRoot,
            Transform effectParent,
            Camera presentationCamera,
            BossEntranceEffectPreset preset,
            BossEntranceEffectMode mode,
            BossInfinityParticleWarmup particleWarmup)
        {
            return PrepareCore(
                subject,
                motionRoot,
                effectParent,
                presentationCamera,
                preset,
                mode,
                particleWarmup);
        }

        private static BossEntranceEffect PrepareCore(
            SpriteRenderer subject,
            Transform motionRoot,
            Transform effectParent,
            Camera presentationCamera,
            BossEntranceEffectPreset preset,
            BossEntranceEffectMode mode,
            BossInfinityParticleWarmup particleWarmup)
        {
            if (subject == null || motionRoot == null ||
                motionRoot == subject.transform ||
                !subject.transform.IsChildOf(motionRoot))
            {
                return null;
            }

            if (preset == null && particleWarmup != null)
            {
                // Production preparation is cache-only. If asynchronous preset
                // loading missed the arrival boundary, the coordinator reveals
                // the static Boss instead of synchronously loading here.
                if (!particleWarmup.TryGetPreparedPreset(out preset))
                    return null;
            }
            else if (preset == null &&
                     !BossEntranceEffectPresetCatalog.TryGetDefault(
                         out preset,
                         out string presetError))
            {
                Debug.LogError(presetError);
                return null;
            }
            if (preset == null || !preset.TryValidate(out _))
                return null;

            var host = new GameObject("BossEntranceEffect");
            if (effectParent != null)
                host.transform.SetParent(effectParent, false);

            BossEntranceEffect effect =
                host.AddComponent<BossEntranceEffect>();
            effect.CaptureAndHide(
                subject,
                motionRoot,
                presentationCamera,
                preset,
                mode,
                particleWarmup);
            return effect;
        }

        public void Play(Action onLanding, Action onComplete)
        {
            if (!IsPrepared)
            {
                onComplete?.Invoke();
                return;
            }

            if (IsPlaying)
            {
                _onComplete += onComplete;
                return;
            }

            if (IsComplete || _subject == null)
            {
                onComplete?.Invoke();
                return;
            }

            _onLanding = onLanding;
            _onComplete = onComplete;
            IsPlaying = true;
            _restored = false;
            _subject.enabled = true;
            SetCompanionRenderersEnabled(false);
            _subject.color = WithAlpha(_originalColor, 0f);
            Vector3 startLocalPosition = _mode !=
                BossEntranceEffectMode.ImpactDrop
                    ? _originalMotionLocalPosition
                    : CalculateStartLocalPosition();
            _motionRoot.localPosition = startLocalPosition;
            _motionRoot.localRotation = _originalMotionLocalRotation;
            float startScale = _mode !=
                BossEntranceEffectMode.ImpactDrop
                    ? 1f
                    : _preset.startScale;
            _motionRoot.localScale = ScaleOriginal(startScale, startScale);

            if (_mode == BossEntranceEffectMode.SpiritConvergence)
                CreateConvergenceParticles();
            else if (_mode == BossEntranceEffectMode.WaterConvergence)
                _waterEffect = BossWaterConvergenceEffect.Create(
                    transform, _subject, _preset);

            _routine = StartCoroutine(
                AnimateEntrance(startLocalPosition));
        }

        /// <summary>
        /// Restores the captured pose before an owner explicitly cancels and
        /// destroys the effect. OnDestroy remains a safety net for scene unload.
        /// </summary>
        public void CancelAndRestore()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            RestoreSubject();
            StopConvergenceParticles();
            ReleaseWaterEffect();
            _onLanding = null;
            _onComplete = null;
            IsPlaying = false;
        }

        private void CaptureAndHide(
            SpriteRenderer subject,
            Transform motionRoot,
            Camera presentationCamera,
            BossEntranceEffectPreset preset,
            BossEntranceEffectMode mode,
            BossInfinityParticleWarmup particleWarmup)
        {
            _subject = subject;
            _motionRoot = motionRoot;
            _presentationCamera = presentationCamera;
            _preset = preset;
            _mode = mode;
            _particleWarmup = particleWarmup;
            _originalMotionLocalPosition = _motionRoot.localPosition;
            _originalMotionLocalScale = _motionRoot.localScale;
            _originalMotionLocalRotation = _motionRoot.localRotation;
            _originalColor = subject.color;
            _originalRendererEnabled = subject.enabled;
            CaptureAndHideCompanionRenderers();
            subject.enabled = false;
            IsPrepared = true;
        }

        private void CaptureAndHideCompanionRenderers()
        {
            Renderer[] renderers =
                _motionRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer == _subject) continue;
                _companionRenderers.Add(renderer);
                _companionRendererStates.Add(renderer.enabled);
                renderer.enabled = false;
            }
        }

        private IEnumerator AnimateEntrance(Vector3 startLocalPosition)
        {
            if (_mode == BossEntranceEffectMode.SpiritConvergence &&
                _landingParticles != null)
            {
                // 灵气真正完成首次发射后才启动显形时钟，避免原生系统四帧
                // 安全初始化把“汇聚”和“BOSS 出现”错开成两件无关的事。
                float initializationWait = 0f;
                const float initializationTimeout = 0.5f;
                while (_landingParticles != null &&
                       !_landingParticles.HasEmitted &&
                       initializationWait < initializationTimeout)
                {
                    yield return null;
                    initializationWait += Mathf.Clamp(
                        Time.unscaledDeltaTime,
                        0f,
                        0.1f);
                }
            }

            float elapsed = 0f;
            float duration = _mode == BossEntranceEffectMode.SpiritConvergence
                ? _preset.convergenceDuration
                : _mode == BossEntranceEffectMode.WaterConvergence
                    ? _preset.waterDuration : _preset.duration;
            float landingTime = _mode == BossEntranceEffectMode.SpiritConvergence
                ? duration * _preset.convergenceRevealNormalized
                : _mode == BossEntranceEffectMode.WaterConvergence
                    ? duration * 0.68f : _preset.LandingTime;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                ApplyFrame(
                    Mathf.Min(elapsed, duration),
                    startLocalPosition);

                if (!_landingSignaled && elapsed >= landingTime)
                    SignalLanding();

                yield return null;
            }

            if (!_landingSignaled)
                SignalLanding();
            Complete();
        }

        private void ApplyFrame(
            float elapsed,
            Vector3 startLocalPosition)
        {
            BossEntranceEffectSample sample =
                BossEntranceEffectSampler.Sample(_preset, elapsed, _mode);
            _motionRoot.localPosition = Vector3.LerpUnclamped(
                startLocalPosition,
                _originalMotionLocalPosition,
                sample.PositionProgress);

            _motionRoot.localScale = ScaleOriginal(
                sample.Scale.x,
                sample.Scale.y);
            _motionRoot.localRotation = _originalMotionLocalRotation;
            Color silhouette = new Color(
                _originalColor.r * 0.16f,
                _originalColor.g * 0.16f,
                _originalColor.b * 0.18f,
                _originalColor.a);
            Color bodyColor = Color.Lerp(
                silhouette,
                _originalColor,
                sample.BodyAlpha);
            bodyColor.a = _originalColor.a *
                (_mode != BossEntranceEffectMode.ImpactDrop
                    ? sample.BodyAlpha
                    : Mathf.Lerp(0.18f, 1f, sample.BodyAlpha));
            _subject.color = bodyColor;
            _waterEffect?.Apply(elapsed / Mathf.Max(0.01f, _preset.waterDuration));
        }

        private void SignalLanding()
        {
            if (_landingSignaled)
                return;
            _landingSignaled = true;
            LandingCount++;
            if (_mode == BossEntranceEffectMode.ImpactDrop)
                CreateLandingParticles();
            try
            {
                _onLanding?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void Complete()
        {
            _routine = null;
            RestoreSubject();
            ReleaseWaterEffect();
            if (_mode == BossEntranceEffectMode.SpiritConvergence)
            {
                // 已发射的 Infinity 尾迹按自身 lifetime 自然收束；取消时才强停。
                _landingParticles = null;
            }
            IsPlaying = false;
            IsComplete = true;

            Action complete = _onComplete;
            _onLanding = null;
            _onComplete = null;
            complete?.Invoke();
        }

        private void CreateLandingParticles()
        {
            if (_subject == null) return;

            Bounds bounds = _subject.bounds;
            Vector3 origin = new Vector3(
                bounds.center.x,
                bounds.min.y + 0.05f,
                bounds.center.z);
            int particleCount = Mathf.Clamp(_preset.particleCount, 0, 320);
            float footprint = Mathf.Max(0.35f, bounds.extents.x * 0.78f);
            float verticalUnit = Mathf.Max(0.24f, bounds.size.y * 0.18f);
            float baseSize = Mathf.Clamp(
                bounds.size.y * 0.0085f,
                0.024f,
                0.06f);
            var particles = new List<BossInfinityParticleSeed>(particleCount);
            for (int i = 0; i < particleCount; i++)
            {
                BossEntranceParticleSeed seed =
                    BossEntranceParticlePattern.GetSeed(_preset, i);
                particles.Add(new BossInfinityParticleSeed(
                    new Vector3(
                        seed.PositionUnit.x * footprint,
                        seed.PositionUnit.y * verticalUnit,
                        0f),
                    new Vector3(
                        seed.VelocityUnit.x * footprint,
                        seed.VelocityUnit.y * verticalUnit,
                        0f),
                    seed.Lifetime,
                    baseSize * seed.SizeMultiplier,
                    seed.Rotation,
                    seed.Color));
            }
            BossInfinityParticleAppearance appearance =
                ResolveParticleAppearance(
                    _preset,
                    BossEntranceEffectMode.ImpactDrop);
            if (_particleWarmup != null)
            {
                _particleWarmup.TryPlay(
                    origin,
                    "BossEntranceInfinityDust",
                    particles,
                    appearance,
                    _subject.sortingLayerID,
                    _subject.sortingOrder + 2,
                    out _landingParticles);
                return;
            }
            _landingParticles = BossInfinityParticleField.Play(
                transform.parent,
                origin,
                "BossEntranceInfinityDust",
                particles,
                appearance,
                _subject.sortingLayerID,
                _subject.sortingOrder + 2);
        }

        private void ReleaseWaterEffect()
        {
            if (_waterEffect == null) return;
            BossWaterConvergenceEffect effect = _waterEffect;
            _waterEffect = null;
            effect.Release();
            if (Application.isPlaying) Destroy(effect.gameObject);
            else DestroyImmediate(effect.gameObject);
        }

        private void CreateConvergenceParticles()
        {
            if (_subject == null)
                return;
            Bounds bounds = _subject.bounds;
            int count = Mathf.Clamp(
                _preset.convergenceParticleCount,
                32,
                320);
            float height = Mathf.Max(0.55f, bounds.size.y);
            // orbitalZ 是圆形旋涡；X/Y 必须共用同一个尺度，否则宽高分别
            // 拉伸会把解析轨迹扭成椭圆，中心也会看起来对不准。
            float motionScale = Mathf.Max(0.48f, height * 0.72f);
            var particles = new List<BossInfinityParticleSeed>(count);
            for (int i = 0; i < count; i++)
            {
                BossEntranceParticleSeed seed =
                    BossEntranceConvergenceParticlePattern.GetSeed(_preset, i);
                particles.Add(new BossInfinityParticleSeed(
                    new Vector3(
                        seed.PositionUnit.x * motionScale,
                        seed.PositionUnit.y * motionScale,
                        0f),
                    new Vector3(
                        seed.VelocityUnit.x * motionScale,
                        seed.VelocityUnit.y * motionScale,
                        0f),
                    seed.Lifetime,
                    Mathf.Clamp(
                        height * 0.024f * seed.SizeMultiplier,
                        0.05f,
                        0.28f),
                    seed.Rotation,
                    seed.Color,
                    BossEntranceConvergenceParticlePattern.GetVisualVariant(
                        i)));
            }
            BossInfinityParticleAppearance appearance =
                ResolveParticleAppearance(
                    _preset,
                    BossEntranceEffectMode.SpiritConvergence);
            if (_particleWarmup != null)
            {
                _particleWarmup.TryPlay(
                    bounds.center,
                    "BossEntranceSpiritConvergence",
                    particles,
                    appearance,
                    _subject.sortingLayerID,
                    _subject.sortingOrder - 1,
                    out _landingParticles);
                return;
            }
            _landingParticles = BossInfinityParticleField.Play(
                transform.parent,
                bounds.center,
                "BossEntranceSpiritConvergence",
                particles,
                appearance,
                _subject.sortingLayerID,
                _subject.sortingOrder - 1);
        }

        internal static BossInfinityParticleAppearance ResolveParticleAppearance(
            BossEntranceEffectPreset preset,
            BossEntranceEffectMode mode)
        {
            if (mode == BossEntranceEffectMode.SpiritConvergence)
            {
                return BossEntranceConvergenceParticlePattern.GetAppearance(
                    preset);
            }
            return new BossInfinityParticleAppearance(
                new Color(0.045f, 0.036f, 0.032f, 1f),
                new Color(0.22f, 0.14f, 0.075f, 1f),
                new Color(0.66f, 0.40f, 0.14f, 1f),
                preset != null ? preset.particleOpacity : 1f,
                0.68f,
                0.08f);
        }

        private void StopConvergenceParticles()
        {
            if (_mode != BossEntranceEffectMode.SpiritConvergence ||
                _landingParticles == null)
            {
                return;
            }
            BossInfinityParticleField particles = _landingParticles;
            _landingParticles = null;
            particles.StopAndDestroy();
        }

        private Vector3 CalculateStartLocalPosition()
        {
            Transform parent = _motionRoot.parent;
            Vector3 landingWorld = parent != null
                ? parent.TransformPoint(_originalMotionLocalPosition)
                : _originalMotionLocalPosition;
            float worldDistance = _subject.sprite == null
                ? 0.85f
                : Mathf.Clamp(
                    _subject.bounds.size.y *
                    _preset.dropDistanceInBossHeights,
                    0.42f,
                    1.35f);
            Vector3 startWorld = landingWorld + Vector3.up * worldDistance;
            if (_presentationCamera != null)
            {
                Vector3 viewport =
                    _presentationCamera.WorldToViewportPoint(startWorld);
                if (viewport.z > 0.0001f && viewport.y > 0.92f)
                {
                    viewport.y = 0.92f;
                    startWorld =
                        _presentationCamera.ViewportToWorldPoint(viewport);
                }
            }

            return parent != null
                ? parent.InverseTransformPoint(startWorld)
                : startWorld;
        }

        private Vector3 ScaleOriginal(float x, float y)
        {
            return new Vector3(
                _originalMotionLocalScale.x * x,
                _originalMotionLocalScale.y * y,
                _originalMotionLocalScale.z);
        }

        private void RestoreSubject()
        {
            if (_restored || _subject == null) return;
            _restored = true;
            _motionRoot.localPosition = _originalMotionLocalPosition;
            _motionRoot.localScale = _originalMotionLocalScale;
            _motionRoot.localRotation = _originalMotionLocalRotation;
            _subject.color = _originalColor;
            _subject.enabled = _originalRendererEnabled;
            SetCompanionRenderersEnabled(true);
        }

        private void SetCompanionRenderersEnabled(bool restoreCapturedState)
        {
            for (int i = 0; i < _companionRenderers.Count; i++)
            {
                Renderer renderer = _companionRenderers[i];
                if (renderer != null)
                {
                    renderer.enabled = restoreCapturedState
                        ? _companionRendererStates[i]
                        : false;
                }
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private void OnDestroy()
        {
            CancelAndRestore();
        }
    }
}
