using System.Collections;
using Tuanjie.Infinity;
using UnityEngine;
using UnityEngine.Infinity;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 一档奖励揭示的序列化 Infinity 资源门面。它独占播放生命周期和完整性
    /// 校验，调用者不需要知道某一品阶由几套粒子组成。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RewardRevealInfinityProfile : MonoBehaviour
    {
        public const int StandardSpiritAuraVisualLanguageVersion = 3;
        private const int MinimumEntranceLayerCount = 4;

        [SerializeField] private RewardRarity rarity = RewardRarity.Common;
        [SerializeField, Min(1)] private int visualLanguageVersion =
            StandardSpiritAuraVisualLanguageVersion;
        [SerializeField] private InfinityParticleSystem[] entranceSystems =
            System.Array.Empty<InfinityParticleSystem>();
        [SerializeField] private InfinityParticleSystem[] hoverSystems =
            System.Array.Empty<InfinityParticleSystem>();
        [SerializeField, Min(0.1f)] private float expectedDuration = 1.45f;
        [SerializeField, Min(1)] private int peakParticleBudget = 24;

        private bool _entrancePlayed;
        private bool _entrancePending;
        private bool _runtimeReady;
        private bool _gradeLanguageApplied;
        private Coroutine _runtimeActivation;
        private Material _runtimeSpiritMaterial;

        public RewardRarity Rarity => rarity;
        public int VisualLanguageVersion => visualLanguageVersion;
        public float ExpectedDuration =>
            RewardRevealSpiritGradeLanguage.Resolve(rarity).ExpectedDuration;
        public int PeakParticleBudget =>
            RewardRevealSpiritGradeLanguage.Resolve(rarity).PeakBudget;
        public int EntranceSystemCount => entranceSystems?.Length ?? 0;
        public int HoverSystemCount => hoverSystems?.Length ?? 0;

        public bool IsConfiguredFor(
            RewardRarity expectedRarity,
            out string reason)
        {
            if (rarity != expectedRarity)
            {
                reason = $"Profile 品阶为 {rarity}，预期为 {expectedRarity}。";
                return false;
            }
            if (visualLanguageVersion !=
                StandardSpiritAuraVisualLanguageVersion)
            {
                reason =
                    $"视觉语法版本为 {visualLanguageVersion}，预期为 " +
                    $"{StandardSpiritAuraVisualLanguageVersion}。";
                return false;
            }
            if (expectedDuration <= 0f || expectedDuration > 1.65f)
            {
                reason = $"入场时长 {expectedDuration:0.###} 秒不在 (0, 1.65] 内。";
                return false;
            }
            if (peakParticleBudget <= 0)
            {
                reason = "峰值粒子预算必须大于 0。";
                return false;
            }
            if (entranceSystems == null ||
                entranceSystems.Length < MinimumEntranceLayerCount)
            {
                reason =
                    $"统一灵气入场至少需要 {MinimumEntranceLayerCount} 个纵深层。";
                return false;
            }
            if (!ValidateSystems(entranceSystems, "入场", out reason))
                return false;
            if (!ValidateSystems(hoverSystems, "悬停", out reason))
                return false;

            reason = string.Empty;
            return true;
        }

        public void PlayEntrance()
        {
            if (_entrancePlayed || _entrancePending)
                return;
            _entrancePending = true;
            if (_runtimeReady)
                BeginEntrance();
            else
                ScheduleRuntimeActivation();
        }

        public void PlayHoverPulse()
        {
            if (!_entrancePlayed || !_runtimeReady)
                return;
            if (AnyAlive(hoverSystems))
                return;
            Play(hoverSystems);
        }

        public void StopAndClear()
        {
            if (_runtimeReady)
            {
                StopAndClear(entranceSystems);
                StopAndClear(hoverSystems);
            }
            _entrancePlayed = false;
            _entrancePending = false;
        }

        private void ScheduleRuntimeActivation()
        {
            if (!Application.isPlaying || !isActiveAndEnabled ||
                _runtimeActivation != null)
                return;
            _runtimeActivation = StartCoroutine(ActivateRuntimeAfterEnable());
        }

        private IEnumerator ActivateRuntimeAfterEnable()
        {
            // Runtime modules are allocated by Infinity.OnEnable. A Unity active
            // flag is not a readiness signal. Two stable frames keep the grade
            // recipe write away from the same stack that rebuilt native data.
            yield return null;
            yield return null;
            _runtimeActivation = null;
            if (!isActiveAndEnabled)
                yield break;

            _runtimeReady = true;
            ApplyGradeLanguage();
            if (_entrancePending)
                BeginEntrance();
        }

        private void ApplyGradeLanguage()
        {
            if (!_runtimeReady || _gradeLanguageApplied)
                return;

            RewardSpiritGradeStyle grade =
                RewardRevealSpiritGradeLanguage.Resolve(rarity);
            bool hasReturningSystem = ContainsLayer(
                entranceSystems,
                RewardSpiritLayerKind.Returning);
            if (_runtimeSpiritMaterial == null)
            {
                _runtimeSpiritMaterial =
                    SpiritQiParticleStyle.CreateRuntimeMaterial(
                        $"RewardSpiritQi_{rarity}");
            }
            ConfigureSystems(
                entranceSystems,
                grade,
                hasReturningSystem ? 0 : grade.ReturningCount,
                _runtimeSpiritMaterial);
            ConfigureSystems(
                hoverSystems,
                grade,
                0,
                _runtimeSpiritMaterial);
            _gradeLanguageApplied = true;
        }

        private static void ConfigureSystems(
            InfinityParticleSystem[] systems,
            RewardSpiritGradeStyle grade,
            int returningCountFallback,
            Material spiritMaterial)
        {
            if (systems == null)
                return;

            for (int i = 0; i < systems.Length; i++)
            {
                InfinityParticleSystem system = systems[i];
                if (!CanUseRuntimeState(system) ||
                    !RewardRevealSpiritGradeLanguage.TryResolveLayerKind(
                        system.name,
                        out RewardSpiritLayerKind kind))
                {
                    continue;
                }

                RewardSpiritLayerStyle layer =
                    RewardRevealSpiritGradeLanguage.ResolveLayer(grade, kind);
                int count = layer.Count;
                int cycleCount = layer.CycleCount;
                float repeatInterval = layer.RepeatInterval;
                int echoCount = 0;
                int echoCycleCount = 1;
                float echoTime = 0f;
                float echoRepeatInterval = 0.01f;
                if (returningCountFallback > 0 &&
                    (kind == RewardSpiritLayerKind.ConvergingLeft ||
                     kind == RewardSpiritLayerKind.ConvergingRight))
                {
                    // Older Rare assets have no ReturningQi slot. Preserve the
                    // grade's second beat as a mirrored convergence echo rather
                    // than dumping the missing particles into AscendingQi.
                    RewardSpiritLayerStyle echo =
                        RewardRevealSpiritGradeLanguage.ResolveLayer(
                            grade,
                            RewardSpiritLayerKind.Returning);
                    echoCount = Mathf.CeilToInt(
                        returningCountFallback * 0.5f);
                    echoCycleCount = echo.CycleCount;
                    echoTime = echo.BurstTime;
                    echoRepeatInterval = echo.RepeatInterval;
                }
                ApplyLayer(
                    system,
                    layer,
                    count,
                    cycleCount,
                    repeatInterval,
                    echoCount,
                    echoCycleCount,
                    echoTime,
                    echoRepeatInterval,
                    spiritMaterial);
            }
        }

        private static void ApplyLayer(
            InfinityParticleSystem system,
            RewardSpiritLayerStyle layer,
            int count,
            int requestedCycleCount,
            float requestedRepeatInterval,
            int requestedEchoCount,
            int requestedEchoCycleCount,
            float requestedEchoTime,
            float requestedEchoRepeatInterval,
            Material spiritMaterial)
        {
            int cycleCount = Mathf.Max(1, requestedCycleCount);
            float repeatInterval = Mathf.Max(
                0.01f,
                requestedRepeatInterval);
            int echoCount = Mathf.Max(0, requestedEchoCount);
            int echoCycleCount = Mathf.Max(1, requestedEchoCycleCount);
            float echoRepeatInterval = Mathf.Max(
                0.01f,
                requestedEchoRepeatInterval);
            RewardSpiritParticleMotion motion = layer.Motion;

            var main = system.main;
            main.loop = false;
            main.prewarm = false;
            main.startLifetime = CreateRange(motion.Lifetime);
            main.startSpeed = new InfinityMinMaxCurve(0f);
            main.startSize3D = false;
            main.startSize = CreateRange(motion.Size);
            main.startRotation3D = false;
            main.startRotation = new InfinityMinMaxCurve(0f);
            main.maxParticles = Mathf.Max(
                main.maxParticles,
                count * cycleCount + echoCount * echoCycleCount + 4);

            var emission = system.emission;
            emission.rateOverTime = new InfinityMinMaxCurve(0f);
            emission.rateOverDistance = new InfinityMinMaxCurve(0f);
            var primaryBurst = new InfinityEmissionBurst
            {
                count = new InfinityMinMaxCurve(count),
                time = layer.BurstTime,
                cycleCount = cycleCount,
                repeatInterval = repeatInterval,
                probability = 1f
            };
            if (echoCount > 0)
            {
                emission.SetBursts(new[]
                {
                    primaryBurst,
                    new InfinityEmissionBurst
                    {
                        count = new InfinityMinMaxCurve(echoCount),
                        time = Mathf.Max(0f, requestedEchoTime),
                        cycleCount = echoCycleCount,
                        repeatInterval = echoRepeatInterval,
                        probability = 1f
                    }
                });
            }
            else
            {
                emission.SetBursts(new[] { primaryBurst });
            }

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = InfinityShapeType.Circle;
            shape.radius = layer.Radius;
            shape.radiusThickness = layer.RadiusThickness;
            shape.arcValue = layer.Arc;
            shape.arcMode = InfinityMultiModeValue.BurstSpread;
            shape.arcSpread = 1f / Mathf.Max(1, count);
            shape.rotation = new Vector3(
                0f,
                0f,
                motion.ShapeRotationZ);
            shape.scale = new Vector3(
                motion.ShapeScale.x,
                motion.ShapeScale.y,
                1f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = InfinitySimulationSpace.Local;
            velocity.x = CreateRange(motion.LinearX);
            velocity.y = CreateRange(motion.LinearY);
            velocity.z = new InfinityMinMaxCurve(0f);
            velocity.radial = new InfinityMinMaxCurve(layer.RadialVelocity);
            velocity.orbitalZ =
                new InfinityMinMaxCurve(layer.OrbitalVelocity);

            var noise = system.noise;
            noise.enabled = false;

            if (spiritMaterial != null)
            {
                var renderer = system.renderer;
                renderer.material = spiritMaterial;
                renderer.renderMode = InfinityRenderMode.Billboard;
                renderer.lengthScale = 1f;
                renderer.velocityScale = 0f;
            }
        }

        private static InfinityMinMaxCurve CreateRange(Vector2 range)
        {
            return new InfinityMinMaxCurve(
                InfinityCurveStateMode.TwoConstants,
                Mathf.Min(range.x, range.y),
                Mathf.Max(range.x, range.y),
                1f,
                null,
                null);
        }

        private static bool ContainsLayer(
            InfinityParticleSystem[] systems,
            RewardSpiritLayerKind expected)
        {
            if (systems == null)
                return false;
            for (int i = 0; i < systems.Length; i++)
            {
                InfinityParticleSystem system = systems[i];
                if (system != null &&
                    RewardRevealSpiritGradeLanguage.TryResolveLayerKind(
                        system.name,
                        out RewardSpiritLayerKind kind) &&
                    kind == expected)
                {
                    return true;
                }
            }
            return false;
        }

        private void BeginEntrance()
        {
            _entrancePending = false;
            Play(entranceSystems);
            _entrancePlayed = true;
        }

        private bool ValidateSystems(
            InfinityParticleSystem[] systems,
            string label,
            out string reason)
        {
            if (systems == null || systems.Length == 0)
            {
                reason = $"{label} Infinity 系统为空。";
                return false;
            }

            for (int i = 0; i < systems.Length; i++)
            {
                InfinityParticleSystem system = systems[i];
                if (system == null)
                {
                    reason = $"{label} Infinity 系统第 {i + 1} 项为空。";
                    return false;
                }
                if (system.transform != transform &&
                    !system.transform.IsChildOf(transform))
                {
                    reason = $"{label} Infinity 系统 {system.name} 不属于此 Profile。";
                    return false;
                }
                if (system.infinityAsset == null)
                {
                    reason = $"{label} Infinity 系统 {system.name} 未绑定 InfinityAsset。";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static void Play(InfinityParticleSystem[] systems)
        {
            if (systems == null)
                return;
            for (int i = 0; i < systems.Length; i++)
            {
                InfinityParticleSystem system = systems[i];
                if (!CanUseRuntimeState(system))
                    continue;
                system.Play(withChildren: false, restart: true);
            }
        }

        private static bool AnyAlive(InfinityParticleSystem[] systems)
        {
            if (systems == null)
                return false;
            for (int i = 0; i < systems.Length; i++)
            {
                InfinityParticleSystem system = systems[i];
                if (!CanUseRuntimeState(system) || system.isStopped)
                    continue;
                if (system.IsAlive(false))
                    return true;
            }
            return false;
        }

        private static void StopAndClear(InfinityParticleSystem[] systems)
        {
            if (systems == null)
                return;
            for (int i = 0; i < systems.Length; i++)
            {
                InfinityParticleSystem system = systems[i];
                if (!CanUseRuntimeState(system) || system.isStopped)
                    continue;
                system.Stop(
                    withChildren: false,
                    stopBehavior:
                        ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private static bool CanUseRuntimeState(InfinityParticleSystem system)
        {
            // This is only a structural check. Callers additionally require the
            // profile's one-frame native readiness gate.
            return system != null &&
                   system.enabled &&
                   system.gameObject.activeInHierarchy;
        }

        private void OnEnable()
        {
            ScheduleRuntimeActivation();
        }

        private void OnDisable()
        {
            if (_runtimeActivation != null)
            {
                StopCoroutine(_runtimeActivation);
                _runtimeActivation = null;
            }
            // Infinity.OnDisable owns native cleanup. Do not touch runtime
            // modules from another OnDisable callback in the same hierarchy.
            _runtimeReady = false;
            _gradeLanguageApplied = false;
            _entrancePending = false;
            _entrancePlayed = false;
        }

        private void OnDestroy()
        {
            if (_runtimeSpiritMaterial != null)
                Destroy(_runtimeSpiritMaterial);
            _runtimeSpiritMaterial = null;
        }
    }
}
