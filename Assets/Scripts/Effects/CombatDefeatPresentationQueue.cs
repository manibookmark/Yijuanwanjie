using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns one death-presentation state machine per stable enemy runtime ID.
    /// Damage, defeat and experience callbacks may arrive in either order;
    /// callers only report facts and never coordinate the temporal latches.
    /// Vacancy, death-tail completion and experience settlement are independent
    /// boundaries. Scene disposal invalidates the current generation and never
    /// publishes callbacks into a later scene.
    /// The queue also owns whether a death subject can be copied: only an
    /// actually rendered subject may leave a proxy. This remains one death
    /// lifecycle boundary; no formation or visibility state is owned here.
    /// </summary>
    internal sealed class CombatDefeatPresentationQueue
    {
        internal readonly struct FatalHitToken
        {
            internal FatalHitToken(
                long sourcePresentationId,
                int pendingHitIndex,
                DamageKind kind,
                AttackEffectCompletion effectsCompletion = null)
            {
                SourcePresentationId = sourcePresentationId;
                PendingHitIndex = pendingHitIndex;
                Kind = kind;
                EffectsCompletion = effectsCompletion;
            }

            internal long SourcePresentationId { get; }
            internal int PendingHitIndex { get; }
            internal DamageKind Kind { get; }
            internal AttackEffectCompletion EffectsCompletion { get; }
            internal bool RequiresVisibleHit =>
                Kind == DamageKind.Direct || Kind == DamageKind.Bounce;
            internal bool WaitsForSourceClose => SourcePresentationId > 0;
        }

        private sealed class DeathRecord
        {
            internal int RuntimeId;
            internal int TargetIndex = -1;
            internal Vector3 WorldAnchor;
            internal VfxProfile Profile;
            internal bool IsBoss;
            internal Transform DeathProxy;
            internal FatalHitToken? FatalHit;
            internal CombatExperienceAward? ExperienceAward;
            internal bool DeathRegistered;
            internal bool HitPresented;
            internal bool SourceClosed;
            internal bool ImmediateFallbackArmed;
            internal bool EventFrameElapsed;
            internal bool ReadyQueued;
            internal bool DeathPlaying;
            internal bool VacancyPublished;
            internal bool TailCompleted;
            internal bool ExperienceRequested;
        }

        private const float DeathCascadeStagger = 0.045f;
        private const float DeathPlayingWatchdog = 5f;

        private readonly MonoBehaviour _host;
        private readonly Func<VisualEffectManager> _managerProvider;
        private readonly Action<int> _subjectVacated;
        private readonly Action<CombatExperienceAward, Vector3, Action>
            _requestExperiencePresentation;
        private readonly Action _stateChanged;
        private readonly Dictionary<int, DeathRecord> _records =
            new Dictionary<int, DeathRecord>();
        private readonly HashSet<long> _closedSources = new HashSet<long>();
        private readonly Queue<int> _readyRuntimeIds = new Queue<int>();
        private readonly HashSet<int> _nextFrameRuntimeIds = new HashSet<int>();
        private readonly List<Coroutine> _deathRoutines = new List<Coroutine>();

        private Coroutine _readySequence;
        private Coroutine _nextFrameSequence;
        private Coroutine _effectCompletionSequence;
        private int _outstandingExperiencePresentations;
        private int _generation = 1;
        private bool _readySequenceRunning;
        private bool _levelUpCheckpointPaused;
        private bool _disposed;

        internal CombatDefeatPresentationQueue(
            MonoBehaviour host,
            Func<VisualEffectManager> managerProvider,
            Action<int> subjectVacated,
            Action<CombatExperienceAward, Vector3, Action>
                requestExperiencePresentation,
            Action stateChanged)
        {
            _host = host;
            _managerProvider = managerProvider;
            _subjectVacated = subjectVacated;
            _requestExperiencePresentation = requestExperiencePresentation;
            _stateChanged = stateChanged;
        }

        internal int PendingCount
        {
            get
            {
                int count = 0;
                foreach (DeathRecord record in _records.Values)
                {
                    if (record.DeathRegistered && !record.VacancyPublished)
                        count++;
                }
                return count;
            }
        }

        internal bool IsLevelUpCheckpointPaused => _levelUpCheckpointPaused;
        internal bool IsBusy
        {
            get
            {
                if (_readySequenceRunning || _nextFrameSequence != null ||
                    _readyRuntimeIds.Count > 0 ||
                    _outstandingExperiencePresentations > 0)
                {
                    return true;
                }

                foreach (DeathRecord record in _records.Values)
                {
                    if ((record.DeathRegistered && !record.TailCompleted) ||
                        (record.ExperienceAward.HasValue &&
                         !record.ExperienceRequested))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        internal void SetLevelUpCheckpointPaused(bool paused)
        {
            _levelUpCheckpointPaused = paused;
        }

        internal void ObserveFatalHit(int enemyRuntimeId, FatalHitToken token)
        {
            if (_disposed || enemyRuntimeId <= 0) return;
            DeathRecord record = GetOrCreate(enemyRuntimeId);
            if (!record.FatalHit.HasValue)
                record.FatalHit = token;
            token = record.FatalHit.Value;
            record.SourceClosed = !token.WaitsForSourceClose ||
                _closedSources.Contains(token.SourcePresentationId);
            Evaluate(record);
        }

        internal void RegisterDeath(
            int targetIndex,
            int enemyRuntimeId,
            VfxProfile profile,
            bool isBoss = false)
        {
            if (_disposed || enemyRuntimeId <= 0) return;
            DeathRecord record = GetOrCreate(enemyRuntimeId);
            if (record.DeathRegistered) return;

            record.DeathRegistered = true;
            record.TargetIndex = targetIndex;
            record.Profile = profile;
            record.IsBoss = isBoss;
            VisualEffectManager manager = _managerProvider?.Invoke();
            Transform target = manager != null && targetIndex >= 0
                ? manager.GetEnemyTransform(targetIndex)
                : null;
            record.WorldAnchor = ResolveEnemyCenter(target);
            record.DeathProxy = CreateDeathProxy(
                manager,
                target,
                enemyRuntimeId);
            // Match hidden-row deaths (which have no target): experience must
            // not originate from an invisible reserve's off-board staging point.
            if (record.DeathProxy == null)
                record.WorldAnchor = Vector3.zero;
            if (record.FatalHit.HasValue)
            {
                FatalHitToken token = record.FatalHit.Value;
                record.SourceClosed = !token.WaitsForSourceClose ||
                    _closedSources.Contains(token.SourcePresentationId);
            }
            else
            {
                ArmNextEventFrameFallback(record);
            }
            Evaluate(record);
            _stateChanged?.Invoke();
        }

        internal void MarkHitPresented(
            long sourcePresentationId,
            int pendingHitIndex)
        {
            if (_disposed) return;
            var records = new List<DeathRecord>(_records.Values);
            for (int i = 0; i < records.Count; i++)
            {
                DeathRecord record = records[i];
                if (!record.FatalHit.HasValue) continue;
                FatalHitToken token = record.FatalHit.Value;
                if (token.SourcePresentationId != sourcePresentationId ||
                    token.PendingHitIndex != pendingHitIndex)
                {
                    continue;
                }

                record.HitPresented = true;
                Evaluate(record);
            }
        }

        internal void CloseSource(long sourcePresentationId)
        {
            if (_disposed || sourcePresentationId <= 0) return;
            _closedSources.Add(sourcePresentationId);
            var records = new List<DeathRecord>(_records.Values);
            for (int i = 0; i < records.Count; i++)
            {
                DeathRecord record = records[i];
                if (!record.FatalHit.HasValue ||
                    record.FatalHit.Value.SourcePresentationId !=
                    sourcePresentationId)
                {
                    continue;
                }
                record.SourceClosed = true;
                Evaluate(record);
                TryDispatchExperience(record);
            }
            _stateChanged?.Invoke();
        }

        internal void RecordExperienceAward(CombatExperienceAward award)
        {
            if (_disposed || award.SourceEnemyRuntimeId <= 0) return;
            DeathRecord record = GetOrCreate(award.SourceEnemyRuntimeId);
            record.ExperienceAward = award;
            TryDispatchExperience(record);
            _stateChanged?.Invoke();
        }

        private DeathRecord GetOrCreate(int enemyRuntimeId)
        {
            if (_records.TryGetValue(enemyRuntimeId, out DeathRecord record))
                return record;
            record = new DeathRecord { RuntimeId = enemyRuntimeId };
            _records.Add(enemyRuntimeId, record);
            return record;
        }

        private void Evaluate(DeathRecord record)
        {
            if (_disposed || record == null || !record.DeathRegistered ||
                record.DeathPlaying || record.ReadyQueued ||
                record.TailCompleted)
            {
                return;
            }

            if (!record.FatalHit.HasValue)
            {
                ArmNextEventFrameFallback(record);
                return;
            }

            FatalHitToken token = record.FatalHit.Value;
            if (!token.WaitsForSourceClose && !record.EventFrameElapsed)
            {
                ArmNextEventFrameFallback(record);
                return;
            }
            if (token.RequiresVisibleHit && !record.HitPresented &&
                !record.SourceClosed)
            {
                return;
            }
            if (!token.RequiresVisibleHit && token.WaitsForSourceClose &&
                !record.SourceClosed)
            {
                return;
            }

            // An arrival is not the end of an attack: further projectiles,
            // deferred combo receipts and the actual contact fragments still
            // belong to this source. Legacy/non-card receipts have no ticket.
            if (token.EffectsCompletion != null &&
                (!record.SourceClosed || !token.EffectsCompletion.IsComplete))
            {
                if (record.SourceClosed) StartEffectCompletionWait();
                return;
            }
            QueueReady(record);
        }

        private void StartEffectCompletionWait()
        {
            if (_effectCompletionSequence != null || _host == null ||
                !_host.isActiveAndEnabled || _disposed) return;
            _effectCompletionSequence = _host.StartCoroutine(
                WaitForEffectCompletion(_generation));
        }

        private IEnumerator WaitForEffectCompletion(int generation)
        {
            // Always yield before evaluating, including on an empty ticket.
            // This keeps StartCoroutine's returned handle from resurrecting a
            // synchronously finished waiter.
            yield return null;
            while (IsCurrent(generation))
            {
                bool waiting = false;
                var records = new List<DeathRecord>(_records.Values);
                for (int i = 0; i < records.Count; i++)
                {
                    DeathRecord record = records[i];
                    if (!record.DeathRegistered || record.TailCompleted ||
                        record.DeathPlaying || record.ReadyQueued ||
                        !record.SourceClosed || !record.FatalHit.HasValue ||
                        record.FatalHit.Value.EffectsCompletion == null) continue;
                    Evaluate(record);
                    if (!IsCurrent(generation)) yield break;
                    if (!record.DeathPlaying && !record.ReadyQueued &&
                        !record.TailCompleted) waiting = true;
                }
                if (!waiting) break;
                yield return null;
            }
            if (IsCurrent(generation)) _effectCompletionSequence = null;
        }

        private void ArmNextEventFrameFallback(DeathRecord record)
        {
            if (_disposed || record.ImmediateFallbackArmed) return;
            record.ImmediateFallbackArmed = true;
            _nextFrameRuntimeIds.Add(record.RuntimeId);
            if (_nextFrameSequence == null && _host != null &&
                _host.isActiveAndEnabled)
            {
                int generation = _generation;
                _nextFrameSequence = _host.StartCoroutine(
                    ReleaseNextEventFrame(generation));
            }
            else if (_host == null || !_host.isActiveAndEnabled)
            {
                CompleteWithoutVisual(record);
            }
        }

        private IEnumerator ReleaseNextEventFrame(int generation)
        {
            yield return null;
            if (!IsCurrent(generation)) yield break;

            var runtimeIds = new List<int>(_nextFrameRuntimeIds);
            _nextFrameRuntimeIds.Clear();
            _nextFrameSequence = null;
            for (int i = 0; i < runtimeIds.Count; i++)
            {
                if (!_records.TryGetValue(runtimeIds[i], out DeathRecord record))
                    continue;
                record.ImmediateFallbackArmed = false;
                record.EventFrameElapsed = true;
                if (record.FatalHit.HasValue)
                    Evaluate(record);
                else
                    QueueReady(record);
            }
        }

        private void QueueReady(DeathRecord record)
        {
            if (_disposed || record.ReadyQueued || record.DeathPlaying ||
                record.TailCompleted)
            {
                return;
            }
            record.ReadyQueued = true;
            _readyRuntimeIds.Enqueue(record.RuntimeId);
            StartReadySequence();
        }

        private void StartReadySequence()
        {
            if (_disposed || _readySequenceRunning || _host == null ||
                !_host.isActiveAndEnabled)
            {
                if ((_host == null || !_host.isActiveAndEnabled) &&
                    _readyRuntimeIds.Count > 0)
                {
                    while (_readyRuntimeIds.Count > 0)
                    {
                        int runtimeId = _readyRuntimeIds.Dequeue();
                        if (_records.TryGetValue(runtimeId, out DeathRecord record))
                            CompleteWithoutVisual(record);
                    }
                }
                return;
            }
            int generation = _generation;
            _readySequenceRunning = true;
            Coroutine started = _host.StartCoroutine(
                RunReadySequence(generation));
            // StartCoroutine may finish synchronously on a null-manager
            // fallback. Do not resurrect a completed sequence with its stale
            // returned handle, otherwise later deaths can never start.
            if (_readySequenceRunning)
                _readySequence = started;
        }

        private IEnumerator RunReadySequence(int generation)
        {
            while (IsCurrent(generation) && _readyRuntimeIds.Count > 0)
            {
                while (IsCurrent(generation) && _levelUpCheckpointPaused)
                    yield return null;
                if (!IsCurrent(generation)) yield break;

                int runtimeId = _readyRuntimeIds.Dequeue();
                if (_records.TryGetValue(runtimeId, out DeathRecord record))
                {
                    record.ReadyQueued = false;
                    record.DeathPlaying = true;
                    Coroutine routine = _host.StartCoroutine(
                        PresentDeath(record, generation));
                    _deathRoutines.Add(routine);
                }

                float elapsed = 0f;
                while (IsCurrent(generation) &&
                       _readyRuntimeIds.Count > 0 &&
                       elapsed < DeathCascadeStagger)
                {
                    if (!_levelUpCheckpointPaused && Time.timeScale > 0f)
                        elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (IsCurrent(generation))
            {
                _readySequenceRunning = false;
                _readySequence = null;
                _stateChanged?.Invoke();
            }
        }

        private IEnumerator PresentDeath(DeathRecord record, int generation)
        {
            bool tailCallback = false;
            VisualEffectManager manager = _managerProvider?.Invoke();
            Transform proxy = record.DeathProxy;
            if (manager == null || proxy == null)
            {
                PublishVacancy(record, generation);
                tailCallback = true;
            }
            else
            {
                manager.PlayDeathEffect(
                    proxy,
                    record.Profile,
                    () => PublishVacancy(record, generation),
                    () =>
                    {
                        if (!IsCurrent(generation)) return;
                        tailCallback = true;
                    },
                    record.IsBoss
                        ? DeathEffectType.BossDisperse
                        : (DeathEffectType?)null);
            }

            float activeElapsed = 0f;
            while (IsCurrent(generation) && !tailCallback &&
                   activeElapsed < DeathPlayingWatchdog)
            {
                if (!_levelUpCheckpointPaused && Time.timeScale > 0f)
                    activeElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!IsCurrent(generation)) yield break;

            if (!tailCallback)
            {
                Debug.LogError(
                    $"[VFX] DeathPlaying watchdog released runtime=" +
                    $"{record.RuntimeId} after {DeathPlayingWatchdog:0.##}s.");
            }
            PublishVacancy(record, generation);
            CompleteTail(record, generation);
        }

        private void CompleteWithoutVisual(DeathRecord record)
        {
            if (_disposed || record == null) return;
            PublishVacancy(record, _generation);
            CompleteTail(record, _generation);
        }

        private void PublishVacancy(DeathRecord record, int generation)
        {
            if (!IsCurrent(generation) || record.VacancyPublished) return;
            record.VacancyPublished = true;
            _subjectVacated?.Invoke(record.RuntimeId);
            _stateChanged?.Invoke();
        }

        private void CompleteTail(DeathRecord record, int generation)
        {
            if (!IsCurrent(generation) || record.TailCompleted) return;
            record.DeathPlaying = false;
            record.TailCompleted = true;
            if (record.DeathProxy != null)
                UnityEngine.Object.Destroy(record.DeathProxy.gameObject);
            record.DeathProxy = null;
            TryDispatchExperience(record);
            TryRemoveCompleted(record);
            _stateChanged?.Invoke();
        }

        private void TryDispatchExperience(DeathRecord record)
        {
            if (_disposed || record == null || !record.ExperienceAward.HasValue ||
                record.ExperienceRequested || !record.TailCompleted ||
                !IsSourceClosed(record))
            {
                return;
            }

            record.ExperienceRequested = true;
            CombatExperienceAward award = record.ExperienceAward.Value;
            Vector3 worldOrigin = record.WorldAnchor;
            _outstandingExperiencePresentations++;
            int generation = _generation;
            bool completed = false;
            Action completion = () =>
            {
                if (completed) return;
                completed = true;
                if (!IsCurrent(generation)) return;
                _outstandingExperiencePresentations = Mathf.Max(
                    0,
                    _outstandingExperiencePresentations - 1);
                TryRemoveCompleted(record);
                _stateChanged?.Invoke();
            };

            if (_requestExperiencePresentation != null)
            {
                _requestExperiencePresentation(award, worldOrigin, completion);
            }
            else
            {
                Debug.LogError(
                    $"[VFX] Deferred experience award {award.AwardId} " +
                    "has no Bootstrap presentation coordinator.");
                completion();
            }
        }

        private bool IsSourceClosed(DeathRecord record)
        {
            if (!record.FatalHit.HasValue) return true;
            FatalHitToken token = record.FatalHit.Value;
            return !token.WaitsForSourceClose || record.SourceClosed ||
                _closedSources.Contains(token.SourcePresentationId);
        }

        private void TryRemoveCompleted(DeathRecord record)
        {
            if (record == null || !record.TailCompleted) return;
            // Keep a lightweight tombstone when no award has arrived yet.
            // Core normally publishes XP in the same event stack, but null-VFX
            // and inactive-host fallbacks may complete synchronously first.
            if (!record.ExperienceAward.HasValue ||
                !record.ExperienceRequested)
                return;
            _records.Remove(record.RuntimeId);
        }

        /// <summary>
        /// Cancels presentation inside a live combat. Unlike scene disposal it
        /// releases each registered vacancy exactly once so formation cannot
        /// deadlock. A new generation makes late effect/XP callbacks harmless.
        /// </summary>
        internal void CancelInField()
        {
            if (_disposed) return;
            unchecked { _generation++; }
            StopOwnedCoroutines();
            _readyRuntimeIds.Clear();
            _nextFrameRuntimeIds.Clear();
            _outstandingExperiencePresentations = 0;

            var records = new List<DeathRecord>(_records.Values);
            for (int i = 0; i < records.Count; i++)
            {
                DeathRecord record = records[i];
                record.FatalHit?.EffectsCompletion?.Cancel();
                if (record.DeathRegistered && !record.VacancyPublished)
                {
                    record.VacancyPublished = true;
                    _subjectVacated?.Invoke(record.RuntimeId);
                }
                record.DeathPlaying = false;
                record.TailCompleted = true;
                if (record.DeathProxy != null)
                    UnityEngine.Object.Destroy(record.DeathProxy.gameObject);
                record.DeathProxy = null;
                TryDispatchExperience(record);
            }
            _stateChanged?.Invoke();
        }

        internal void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            unchecked { _generation++; }

            StopOwnedCoroutines();

            foreach (DeathRecord record in _records.Values)
            {
                record.FatalHit?.EffectsCompletion?.Cancel();
                if (record.DeathProxy != null)
                    UnityEngine.Object.Destroy(record.DeathProxy.gameObject);
            }
            _records.Clear();
            _closedSources.Clear();
            _readyRuntimeIds.Clear();
            _nextFrameRuntimeIds.Clear();
            _deathRoutines.Clear();
            _effectCompletionSequence = null;
            _readySequence = null;
            _nextFrameSequence = null;
            _readySequenceRunning = false;
            _outstandingExperiencePresentations = 0;
        }

        private void StopOwnedCoroutines()
        {
            if (_host != null)
            {
                if (_readySequence != null)
                    _host.StopCoroutine(_readySequence);
                if (_nextFrameSequence != null)
                    _host.StopCoroutine(_nextFrameSequence);
                if (_effectCompletionSequence != null)
                    _host.StopCoroutine(_effectCompletionSequence);
                for (int i = 0; i < _deathRoutines.Count; i++)
                {
                    if (_deathRoutines[i] != null)
                        _host.StopCoroutine(_deathRoutines[i]);
                }
            }
            _deathRoutines.Clear();
            _effectCompletionSequence = null;
            _readySequence = null;
            _nextFrameSequence = null;
            _readySequenceRunning = false;
        }

        private bool IsCurrent(int generation)
            => !_disposed && generation == _generation;

        private static Vector3 ResolveEnemyCenter(Transform target)
        {
            if (target == null) return Vector3.zero;
            SpriteRenderer renderer =
                target.GetComponentInChildren<SpriteRenderer>();
            return renderer != null ? renderer.bounds.center : target.position;
        }

        private static Transform CreateDeathProxy(
            VisualEffectManager manager,
            Transform source,
            int enemyRuntimeId)
        {
            if (source == null) return null;
            SpriteRenderer sourceRenderer = source.GetComponent<SpriteRenderer>();
            if (sourceRenderer == null)
                sourceRenderer = source.GetComponentInChildren<SpriteRenderer>();
            // Core can resolve another hit after backfill has changed its
            // footprint but before the presentation has admitted the actor.
            // Never turn an invisible reserve into a visible death proxy at
            // its old staging position. A null proxy still follows the normal
            // vacancy, tail and experience settlement path in PresentDeath.
            if (sourceRenderer == null || sourceRenderer.sprite == null ||
                !sourceRenderer.enabled ||
                !sourceRenderer.gameObject.activeInHierarchy ||
                sourceRenderer.color.a <= 0f)
            {
                return null;
            }

            var proxyObject = new GameObject(
                $"EnemyDeathProxy_{enemyRuntimeId}",
                typeof(SpriteRenderer));
            if (manager != null && manager.effectContainer != null)
            {
                proxyObject.transform.SetParent(
                    manager.effectContainer,
                    worldPositionStays: false);
            }
            proxyObject.transform.position = sourceRenderer.transform.position;
            proxyObject.transform.rotation = sourceRenderer.transform.rotation;
            Vector3 lossyScale = sourceRenderer.transform.lossyScale;
            Transform parent = proxyObject.transform.parent;
            proxyObject.transform.localScale = parent != null
                ? new Vector3(
                    SafeScale(lossyScale.x, parent.lossyScale.x),
                    SafeScale(lossyScale.y, parent.lossyScale.y),
                    SafeScale(lossyScale.z, parent.lossyScale.z))
                : lossyScale;

            SpriteRenderer proxy = proxyObject.GetComponent<SpriteRenderer>();
            proxy.sprite = sourceRenderer.sprite;
            proxy.color = sourceRenderer.color;
            proxy.flipX = sourceRenderer.flipX;
            proxy.flipY = sourceRenderer.flipY;
            proxy.sortingLayerID = sourceRenderer.sortingLayerID;
            proxy.sortingOrder = sourceRenderer.sortingOrder + 2;
            return proxyObject.transform;
        }

        private static float SafeScale(float worldScale, float parentScale)
            => Mathf.Abs(parentScale) > 0.0001f
                ? worldScale / parentScale
                : worldScale;
    }
}
