using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Effects;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Cross-module owner of enemy experience presentation. World-to-HUD
    /// transfers are intentionally concurrent, while Core settlement, HUD
    /// filling and ordinary level-up panels remain authoritative FIFO.
    /// </summary>
    public sealed class CombatExperiencePresentationCoordinator : MonoBehaviour
    {
        private enum SegmentStage { AwaitingSegment, Committed, WaitingBar, WaitingPanel, FinishSegment, Completed }

        private sealed class PendingPresentation
        {
            public CombatExperienceAward Award;
            public Vector3 WorldOrigin;
            public Action BridgeCompletion;
            public bool TransferCompleted;
            public bool CompletionInvoked;
            public SegmentStage Stage;
            public CombatExperienceSegmentResult Segment;
            public int SegmentGeneration;
            public bool BarRequested, BarCompleted, ReplayBar;
            public bool PanelRequested, PanelClosed;
        }

        private readonly List<PendingPresentation> _pending =
            new List<PendingPresentation>();
        private CombatController _combat;
        private CombatVisualBridge _visualBridge;
        private CombatSceneController _sceneController;
        private BattleHudView _battleHud;
        private Canvas _canvas;
        private Camera _worldCamera;
        private ExperienceTransferEffect _transferEffect;
        private Coroutine _settlementRoutine;
        private bool _destroying;
        private int _workerGeneration;

        public bool IsPresenting { get; private set; }
        public int PendingPresentationCount => _pending.Count;
        public int ActiveTransferCount =>
            _transferEffect != null ? _transferEffect.ActiveTransferCount : 0;

        public event Action<CombatExperienceAward> TransferStarted;
        public event Action<CombatExperienceSegmentResult> SegmentCommitted;
        public event Action LevelUpCheckpointStarted;
        public event Action LevelUpCheckpointCompleted;

        public void Bind(
            CombatController combat,
            CombatVisualBridge visualBridge,
            CombatSceneController sceneController,
            BattleHudView battleHud,
            Canvas canvas,
            Camera worldCamera,
            ExperienceTransferEffect transferEffect)
        {
            if (_combat != null && !ReferenceEquals(_combat, combat))
                CancelPending();
            Unbind();
            _combat = combat;
            _visualBridge = visualBridge;
            _sceneController = sceneController;
            _battleHud = battleHud;
            _canvas = canvas;
            _worldCamera = worldCamera;
            _transferEffect = transferEffect;

            if (_combat == null || _visualBridge == null)
                return;

            if (_combat.ExperienceAwardCommitMode != ExperienceAwardCommitMode.DeferredSegments)
                _combat.SetExperienceAwardCommitMode(ExperienceAwardCommitMode.DeferredSegments);
            _visualBridge.ExperienceAwardPresentationRequested +=
                OnExperienceAwardPresentationRequested;
        }

        public void Unbind()
        {
            if (_visualBridge != null)
            {
                _visualBridge.ExperienceAwardPresentationRequested -=
                    OnExperienceAwardPresentationRequested;
            }
        }

        private void OnExperienceAwardPresentationRequested(
            CombatExperienceAward award,
            Vector3 worldOrigin,
            Action onComplete)
        {
            if (_destroying)
            {
                onComplete?.Invoke();
                return;
            }

            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Award.AwardId != award.AwardId) continue;
                Debug.LogError(
                    $"[CombatExperience] Duplicate presentation request " +
                    $"for award {award.AwardId}.");
                onComplete?.Invoke();
                return;
            }

            var presentation = new PendingPresentation
            {
                Award = award,
                WorldOrigin = worldOrigin,
                BridgeCompletion = onComplete
            };
            _pending.Add(presentation);
            IsPresenting = true;
            TransferStarted?.Invoke(award);
            StartTransfer(presentation);

            EnsureSettlement();
        }

        private void EnsureSettlement()
        {
            if (!_destroying && isActiveAndEnabled && _pending.Count > 0 && _settlementRoutine == null)
            {
                int worker = ++_workerGeneration;
                _settlementRoutine = StartCoroutine(SettleInAwardOrder(worker));
            }
        }

        private void StartTransfer(PendingPresentation presentation)
        {
            RectTransform target = _battleHud != null
                ? _battleHud.ProgressTarget
                : null;
            if (_transferEffect == null || _canvas == null || target == null)
            {
                presentation.TransferCompleted = true;
                return;
            }

            _transferEffect.Play(
                _canvas,
                _worldCamera,
                presentation.WorldOrigin,
                target,
                presentation.Award.ActualExperience,
                () => presentation.TransferCompleted = true);
        }

        private IEnumerator SettleInAwardOrder(int worker)
        {
            // Establish the coroutine handle before any synchronous observer
            // can hide this owner or finish a synthetic no-HUD presentation.
            yield return null;
            while (IsWorkerCurrent(worker) && _pending.Count > 0)
            {
                PendingPresentation head = FindAuthoritativeHead();
                if (head == null || !head.TransferCompleted)
                {
                    yield return null;
                    continue;
                }

                yield return SettlePresentation(head, worker);
                if (!IsWorkerCurrent(worker)) yield break;
                if (head.Stage == SegmentStage.Completed) CompletePresentation(head);
            }

            if (IsWorkerCurrent(worker))
            {
                _settlementRoutine = null;
                IsPresenting = _pending.Count > 0;
            }
        }

        private PendingPresentation FindAuthoritativeHead()
        {
            // Core can dequeue a fully committed award before its bar/panel
            // finishes. Resume that visual checkpoint before the next award.
            foreach (PendingPresentation pending in _pending)
                if (pending.Stage == SegmentStage.Completed) return pending;
            foreach (PendingPresentation pending in _pending)
                if (pending.Stage != SegmentStage.AwaitingSegment) return pending;
            if (_combat == null ||
                !_combat.TryPeekPendingExperienceAward(
                    out CombatExperienceAward authoritative))
            {
                return _pending.Count > 0 ? _pending[0] : null;
            }

            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Award.AwardId == authoritative.AwardId)
                    return _pending[i];
            }
            return null;
        }

        private bool IsWorkerCurrent(int worker) =>
            !_destroying && isActiveAndEnabled && worker == _workerGeneration;

        private bool CanAdvance(PendingPresentation pending, int worker) =>
            IsWorkerCurrent(worker) && _pending.Contains(pending);

        private IEnumerator SettlePresentation(PendingPresentation pending, int worker)
        {
            while (CanAdvance(pending, worker) && pending.Stage != SegmentStage.Completed)
            {
                if (pending.Stage == SegmentStage.AwaitingSegment)
                {
                    while (CanAdvance(pending, worker) &&
                           ((_sceneController != null && _sceneController.IsCardPresentationActive) ||
                            (_combat != null && _combat.AwaitingRewardSelection)))
                        yield return null;
                    if (!CanAdvance(pending, worker)) yield break;
                    _sceneController?.BeginCombatPresentation(lockCombatInput: false);
                    if (_combat == null || !_combat.TryCommitNextExperienceSegment(
                            pending.Award.AwardId, out CombatExperienceSegmentResult committed))
                    {
                        Debug.LogError($"[CombatExperience] Failed to commit head segment for award {pending.Award.AwardId}.");
                        pending.Stage = SegmentStage.Completed;
                        _sceneController?.EndCombatPresentation();
                        yield break;
                    }
                    // Persist the commit before observers can disable this GO.
                    pending.SegmentGeneration++;
                    pending.Segment = committed;
                    pending.Stage = SegmentStage.Committed;
                    if (committed.TriggeredLevelUp && committed.AwaitingRewardSelection)
                        _sceneController?.BeginCombatPresentation(lockCombatInput: true);
                    SegmentCommitted?.Invoke(committed);
                    if (!CanAdvance(pending, worker)) yield break;
                }

                CombatExperienceSegmentResult segment = pending.Segment;
                int segmentGeneration = pending.SegmentGeneration;
                bool checkpoint = segment.TriggeredLevelUp && segment.AwaitingRewardSelection;
                if (pending.Stage == SegmentStage.Committed)
                {
                    pending.Stage = SegmentStage.WaitingBar;
                    if (checkpoint) LevelUpCheckpointStarted?.Invoke();
                    else _sceneController?.EndCombatPresentation(refreshDeferredProgress: false);
                    if (!CanAdvance(pending, worker)) yield break;
                }
                if (pending.Stage == SegmentStage.WaitingBar)
                {
                    if (!pending.BarCompleted && (!pending.BarRequested || pending.ReplayBar))
                    {
                        pending.BarRequested = true;
                        pending.ReplayBar = false;
                        if (_battleHud != null)
                            _battleHud.AnimateExperienceSegment(segment, () =>
                            {
                                if (_pending.Contains(pending) && pending.SegmentGeneration == segmentGeneration)
                                    pending.BarCompleted = true;
                            });
                        else pending.BarCompleted = true;
                    }
                    while (CanAdvance(pending, worker) && !pending.BarCompleted) yield return null;
                    if (!CanAdvance(pending, worker)) yield break;
                    pending.Stage = checkpoint ? SegmentStage.WaitingPanel : SegmentStage.FinishSegment;
                }
                if (pending.Stage == SegmentStage.WaitingPanel)
                {
                    if (!pending.PanelRequested)
                    {
                        pending.PanelRequested = true;
                        if (_sceneController != null)
                            _sceneController.PresentDeferredLevelUpCheckpoint(() =>
                            {
                                if (_pending.Contains(pending) && pending.SegmentGeneration == segmentGeneration)
                                    pending.PanelClosed = true;
                            });
                        else pending.PanelClosed = true;
                    }
                    while (CanAdvance(pending, worker) && !pending.PanelClosed) yield return null;
                    if (!CanAdvance(pending, worker)) yield break;
                    pending.Stage = SegmentStage.FinishSegment;
                }
                if (pending.Stage == SegmentStage.FinishSegment)
                {
                    pending.Stage = segment.AwardFullyCommitted
                        ? SegmentStage.Completed : SegmentStage.AwaitingSegment;
                    pending.BarRequested = pending.BarCompleted = pending.ReplayBar = false;
                    pending.PanelRequested = pending.PanelClosed = false;
                    if (checkpoint || segment.TriggeredLevelUp)
                        _battleHud?.SetExperienceSnapshot(segment.LevelAfter,
                            segment.ExperienceAfter, segment.RequiredExperienceAfter);
                    if (checkpoint)
                    {
                        _sceneController?.EndCombatPresentation();
                        LevelUpCheckpointCompleted?.Invoke();
                    }
                    if (!CanAdvance(pending, worker)) yield break;
                    if (pending.Stage != SegmentStage.Completed)
                        yield return new WaitForSecondsRealtime(0.10f);
                }
            }
        }

        private void CompletePresentation(PendingPresentation presentation)
        {
            if (presentation == null || presentation.CompletionInvoked)
                return;
            presentation.CompletionInvoked = true;
            _pending.Remove(presentation);
            Action completion = presentation.BridgeCompletion;
            presentation.BridgeCompletion = null;
            completion?.Invoke();
        }

        private void OnDestroy()
        {
            _destroying = true;
            Unbind();
            CancelPending();
        }

        private void CancelPending()
        {
            _workerGeneration++;
            if (_settlementRoutine != null)
            {
                StopCoroutine(_settlementRoutine);
                _settlementRoutine = null;
            }

            foreach (PendingPresentation pending in _pending)
            {
                pending.CompletionInvoked = true;
                pending.BridgeCompletion = null;
            }
            _pending.Clear();
            _transferEffect?.CancelAll();
            IsPresenting = false;
        }

        private void OnDisable()
        {
            _workerGeneration++;
            if (_settlementRoutine != null) StopCoroutine(_settlementRoutine);
            _settlementRoutine = null;
            foreach (PendingPresentation pending in _pending)
                pending.ReplayBar = pending.BarRequested && !pending.BarCompleted;
        }

        private void OnEnable() => EnsureSettlement();
    }
}
