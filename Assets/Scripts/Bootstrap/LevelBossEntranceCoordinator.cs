using System;
using System.Collections;
using UnityEngine;
using Unity.Profiling;
using WanjieScroll.Audio;
using WanjieScroll.Core;
using WanjieScroll.Effects;
using WanjieScroll.UI.Battle;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    public enum LevelBossEntranceState
    {
        Unavailable = 0,
        WaitingForArrival = 1,
        Reserved = 2,
        WaitingForVisualBoundary = 3,
        Playing = 4,
        Completed = 5,
        Cancelled = 6
    }

    /// <summary>关 BOSS 入场状态的只读诊断快照。</summary>
    public readonly struct LevelBossEntranceSnapshot
    {
        public int BossRuntimeId { get; }
        public int BossIndex { get; }
        public LevelBossEntranceState State { get; }
        public int PresentedCount { get; }
        public int CompletedCount { get; }

        public LevelBossEntranceSnapshot(
            int bossRuntimeId,
            int bossIndex,
            LevelBossEntranceState state,
            int presentedCount,
            int completedCount)
        {
            BossRuntimeId = bossRuntimeId;
            BossIndex = bossIndex;
            State = state;
            PresentedCount = presentedCount;
            CompletedCount = completedCount;
        }
    }

    /// <summary>
    /// 一场战斗中关卡 BOSS 一次性入场表现的唯一生命周期所有者。
    ///
    /// Core 决定何时部署；本模块同步保留角色、等待死亡/补位/升级表现边界，
    /// 再播放落地、音频和震屏。Bootstrap 只注入依赖，不再扫描或保存特效句柄。
    /// </summary>
    public sealed class LevelBossEntranceCoordinator : MonoBehaviour
    {
        private static readonly ProfilerMarker PrepareEntranceMarker =
            new ProfilerMarker("BossEntrance.ReserveAndPrepare");
        private static readonly ProfilerMarker PlayEntranceMarker =
            new ProfilerMarker("BossEntrance.PlayPrepared");

        private CombatController _combat;
        private EnemyAreaView _enemyArea;
        private CombatSceneController _sceneController;
        private VisualEffectManager _effects;
        private CombatVisualBridge _visualBridge;
        private AudioManager _audio;
        private Camera _camera;
        private BossEntranceSpeechView _speechView;
        private BossEntranceEffect _entranceEffect;
        private IDisposable _inputLock;
        private Action _openingContinuation;
        private Coroutine _waitRoutine;
        private Coroutine _tauntWaitRoutine;
        private int _bossIndex = -1;
        private int _bossRuntimeId;
        private string _bossContentId;
        private string _bossVoiceCueId;
        private BossEntranceEffectMode _entranceMode =
            BossEntranceEffectMode.ImpactDrop;
        private bool _openingEntrance;
        private bool _openingContinued;
        private bool _releasedReservation;

        public LevelBossEntranceState State { get; private set; } =
            LevelBossEntranceState.Unavailable;
        public BossEntranceEffect EntranceEffect => _entranceEffect;
        public int PresentedCount { get; private set; }
        public int CompletedCount { get; private set; }
        public LevelBossEntranceSnapshot Snapshot =>
            new LevelBossEntranceSnapshot(
                _bossRuntimeId,
                _bossIndex,
                State,
                PresentedCount,
                CompletedCount);

        public void Bind(
            CombatController combat,
            EnemyAreaView enemyArea,
            CombatSceneController sceneController,
            VisualEffectManager effects,
            CombatVisualBridge visualBridge,
            AudioManager audio,
            Camera presentationCamera,
            BossEntranceSpeechView speechView = null)
        {
            Unbind();
            _combat = combat;
            _enemyArea = enemyArea;
            _sceneController = sceneController;
            _effects = effects;
            _visualBridge = visualBridge;
            _audio = audio;
            _camera = presentationCamera;
            _speechView = speechView;

            if (_combat == null || _enemyArea == null || _effects == null)
                return;

            _bossIndex = FindLevelBossIndex();
            if (_bossIndex < 0) return;

            EnemyStats boss = _combat.Enemies[_bossIndex];
            _bossRuntimeId = boss.runtimeId;
            _bossContentId = boss.contentId;
            ResolveEntrancePresentationMetadata();
            _combat.OnLevelBossArrived += OnLevelBossArrived;
            if (!boss.IsDeployed)
            {
                State = LevelBossEntranceState.WaitingForArrival;
                return;
            }

            _openingEntrance = true;
            ReserveAndPrepare();
        }

        /// <summary>
        /// Starts heavy presentation preparation only after the Combat scene is
        /// visible and its Infinity hierarchy has entered an active ancestry.
        /// Repeated activation calls are idempotent inside the Effects owner.
        /// </summary>
        public void BeginPresentationWarmup()
        {
            if (_bossIndex < 0 || _effects == null)
                return;
            _effects.BeginBossEntranceWarmup(_entranceMode);
            _audio?.BeginBossPresentationAudioWarmup(_bossVoiceCueId);
        }

        /// <summary>
        /// 供开场总流程注入。没有开场 Boss 时立即继续；有已保留 Boss 时只在
        /// 本模块完成一次入场后继续。后续清场入场由内部事件自动触发。
        /// </summary>
        public void ContinueOpeningEntry(Action continuation)
        {
            if (_openingContinued)
            {
                continuation?.Invoke();
                return;
            }

            if (!_openingEntrance ||
                State == LevelBossEntranceState.Unavailable ||
                State == LevelBossEntranceState.WaitingForArrival ||
                State == LevelBossEntranceState.Completed)
            {
                _openingContinued = true;
                continuation?.Invoke();
                return;
            }

            _openingContinuation = continuation;
            PlayPreparedEntrance();
        }

        private int FindLevelBossIndex()
        {
            for (int i = 0; i < _combat.Enemies.Count; i++)
            {
                EnemyStats enemy = _combat.Enemies[i];
                if (enemy != null && enemy.isBoss &&
                    enemy.bossRank == BossRank.Stage && !enemy.IsDead)
                {
                    return i;
                }
            }
            return -1;
        }

        private void OnLevelBossArrived(LevelBossArrivalEvent arrival)
        {
            if (State != LevelBossEntranceState.WaitingForArrival ||
                arrival.BossRuntimeId != _bossRuntimeId ||
                arrival.BossIndex != _bossIndex)
            {
                return;
            }

            _openingEntrance = false;
            _inputLock ??= _sceneController?.AcquireCombatInputLock();
            if (!ReserveAndPrepare())
            {
                CompleteEntrance();
                return;
            }

            State = LevelBossEntranceState.WaitingForVisualBoundary;
            if (isActiveAndEnabled)
                _waitRoutine = StartCoroutine(WaitForVisualBoundary());
            else
                PlayPreparedEntrance();
        }

        private bool ReserveAndPrepare()
        {
            if (_bossIndex < 0 || _enemyArea == null || _effects == null ||
                !_enemyArea.ReserveEnemyEntrance(_bossIndex))
            {
                State = LevelBossEntranceState.Unavailable;
                return false;
            }

            _releasedReservation = false;
            using (PrepareEntranceMarker.Auto())
            {
                _entranceEffect = _effects.PrepareBossEntrance(
                    _bossIndex,
                    _camera,
                    _entranceMode);
            }
            if (_entranceEffect == null)
            {
                ReleaseReservation();
                State = LevelBossEntranceState.Unavailable;
                return false;
            }

            State = LevelBossEntranceState.Reserved;
            return true;
        }

        private IEnumerator WaitForVisualBoundary()
        {
            // 至少让当前 Core 指令返回一帧，避免在最后一只小怪的伤害事件栈内
            // 抢先播放关卡 BOSS。
            yield return null;
            while (ShouldWaitForVisualBoundary())
                yield return null;

            _waitRoutine = null;
            PlayPreparedEntrance();
        }

        private bool ShouldWaitForVisualBoundary()
        {
            return (_visualBridge != null &&
                    _visualBridge.IsPresentationActive) ||
                (_enemyArea != null &&
                    !_enemyArea.IsFormationPresentationSettled) ||
                (_sceneController != null &&
                    (_sceneController.IsAwaitingRewardSelection ||
                     _sceneController.IsCardPresentationActive));
        }

        private void PlayPreparedEntrance()
        {
            if (_entranceEffect == null ||
                State == LevelBossEntranceState.Playing ||
                State == LevelBossEntranceState.Completed)
            {
                CompleteEntrance();
                return;
            }

            State = LevelBossEntranceState.Playing;
            PresentedCount++;
            if (_entranceMode != BossEntranceEffectMode.WaterConvergence)
                _audio?.PlayPreparedBossAppearSfx();
            bool completed = false;
            Action finish = () =>
            {
                if (completed) return;
                completed = true;
                CompleteEntrance();
            };
            Action afterEffect = () =>
            {
                if (_entranceEffect != null &&
                    _entranceEffect.Mode ==
                    BossEntranceEffectMode.SpiritConvergence &&
                    isActiveAndEnabled)
                {
                    _tauntWaitRoutine = StartCoroutine(
                        WaitForEntranceTaunt(finish));
                    return;
                }
                finish();
            };
            using (PlayEntranceMarker.Auto())
            {
                _entranceEffect.Play(
                    () =>
                    {
                        BossEntranceEffectPreset entrancePreset =
                            _entranceEffect != null
                                ? _entranceEffect.Preset
                                : null;
                        if (entrancePreset != null)
                        {
                            if (_entranceEffect != null &&
                                _entranceEffect.Mode !=
                                BossEntranceEffectMode.ImpactDrop)
                            {
                                _enemyArea?.FadeTopFogUpwardForStageBossEntrance(
                                    Mathf.Max(
                                        0.38f,
                                        entrancePreset.cloudDisperseDuration),
                                    entrancePreset.cloudRiseDistance);
                            }
                            else
                            {
                                _enemyArea?.DismissTopFogForStageBossEntrance(
                                    entrancePreset.cloudDisperseDuration,
                                    entrancePreset.cloudDisperseDistance);
                            }
                        }
                        _audio?.PlayPreparedBossMusic();
                        ScreenShake shake = _camera != null
                            ? _camera.GetComponent<ScreenShake>()
                            : null;
                        if (_entranceEffect != null &&
                            _entranceEffect.Mode ==
                            BossEntranceEffectMode.SpiritConvergence)
                        {
                            shake?.Shake(0.12f, 0.06f);
                        }
                        else if (_entranceMode == BossEntranceEffectMode.ImpactDrop)
                        {
                            shake?.Shake(
                                entrancePreset != null
                                    ? entrancePreset.impactShakeDuration
                                    : 0.24f,
                                entrancePreset != null
                                    ? entrancePreset.impactShakeIntensity
                                    : 0.22f);
                        }
                    },
                    afterEffect);
            }
            if (!_entranceEffect.IsPlaying &&
                !_entranceEffect.IsComplete)
            {
                finish();
            }
        }

        private IEnumerator WaitForEntranceTaunt(Action finish)
        {
            const float timeout = 0.6f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                if (_enemyArea != null &&
                    _enemyArea.TryPlayBossEntranceTaunt(
                        _bossIndex,
                        PresentEntranceSpeech,
                        finish))
                {
                    _tauntWaitRoutine = null;
                    yield break;
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _tauntWaitRoutine = null;
            finish?.Invoke();
        }

        private void PresentEntranceSpeech(
            BossEntranceRuntimeDescriptor descriptor)
        {
            if (descriptor == null)
                return;
            _speechView?.Show(
                descriptor.speakerName,
                descriptor.speechText,
                descriptor.speechDuration);
            _audio?.PlayPreparedVoiceCue(descriptor.voiceCueId);
        }

        private void ResolveEntrancePresentationMetadata()
        {
            _entranceMode = BossEntranceEffectMode.ImpactDrop;
            _bossVoiceCueId = null;
            if (!BossAnimationRuntimeCatalog.TryGetManifest(
                    _bossContentId,
                    out BossAnimationRuntimeManifest manifest) ||
                manifest?.entrance == null)
            {
                return;
            }

            if (manifest.entrance.UsesSpiritConvergence)
                _entranceMode = BossEntranceEffectMode.SpiritConvergence;
            else if (manifest.entrance.UsesWaterConvergence)
                _entranceMode = BossEntranceEffectMode.WaterConvergence;
            _bossVoiceCueId = manifest.entrance.voiceCueId;
        }

        private void CompleteEntrance()
        {
            if (State == LevelBossEntranceState.Completed ||
                State == LevelBossEntranceState.Cancelled)
            {
                ContinueOpeningOnce();
                return;
            }

            State = LevelBossEntranceState.Completed;
            CompletedCount++;
            ReleaseReservation();
            _inputLock?.Dispose();
            _inputLock = null;
            ContinueOpeningOnce();
        }

        private void ContinueOpeningOnce()
        {
            if (!_openingEntrance || _openingContinued) return;
            _openingContinued = true;
            Action continuation = _openingContinuation;
            _openingContinuation = null;
            continuation?.Invoke();
        }

        private void ReleaseReservation()
        {
            if (_releasedReservation || _bossIndex < 0) return;
            _releasedReservation = true;
            _enemyArea?.ReleaseEnemyEntrance(_bossIndex);
        }

        private void Unbind()
        {
            if (_combat != null)
                _combat.OnLevelBossArrived -= OnLevelBossArrived;
            if (_waitRoutine != null)
            {
                StopCoroutine(_waitRoutine);
                _waitRoutine = null;
            }
            if (_tauntWaitRoutine != null)
            {
                StopCoroutine(_tauntWaitRoutine);
                _tauntWaitRoutine = null;
            }
            _speechView?.HideImmediate();
            _entranceEffect?.CancelAndRestore();
            ReleaseReservation();
            _inputLock?.Dispose();
            _inputLock = null;
            _openingContinuation = null;
            _combat = null;
            _enemyArea = null;
            _sceneController = null;
            _effects = null;
            _visualBridge = null;
            _audio = null;
            _camera = null;
            _speechView = null;
            _entranceEffect = null;
            _bossIndex = -1;
            _bossRuntimeId = 0;
            _bossContentId = null;
            _bossVoiceCueId = null;
            _entranceMode = BossEntranceEffectMode.ImpactDrop;
            _openingEntrance = false;
            _openingContinued = false;
            _releasedReservation = true;
            PresentedCount = 0;
            CompletedCount = 0;
            State = LevelBossEntranceState.Unavailable;
        }

        private void OnDestroy()
        {
            if (State != LevelBossEntranceState.Completed &&
                State != LevelBossEntranceState.Unavailable)
            {
                State = LevelBossEntranceState.Cancelled;
            }
            Unbind();
        }
    }
}
