using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns the current floor and completed-floor history as one atomic boundary.
    /// Exploration and formations own their frozen choices; this owner stages
    /// both clones alongside the next floor, committing all or none. Profile
    /// assets stay untouched.
    /// No UI, wallet, scene loading or global hosted Run is known here.
    /// </summary>
    internal sealed class DungeonRunFloorProgression
    {
        private readonly DungeonRunSharedState _shared;
        private readonly List<LevelRunSaveData> _completed;
        private DungeonRunExplorationState _exploration;
        private DungeonRunFormationState _formations;
        private DungeonRunSideEventState _sideEvents;
        internal IDungeonRunSideEventContext SideEventContext { get; private set; }
        private Func<IReadOnlyList<DungeonRunArcanaDefinition>> _activeArcanas;
        internal void BindSources(DungeonRunSideEventState state, IDungeonRunSideEventContext context,
            Func<IReadOnlyList<DungeonRunArcanaDefinition>> activeArcanas)
        { _sideEvents = state; SideEventContext = context; _activeArcanas = activeArcanas; }
        internal LevelRunSession Current { get; private set; }
        internal int Seed => _exploration.Seed;
        internal string FrozenEncounterFingerprint => _formations.FrozenFingerprint;
        internal Func<string, CommonEncounterSourceContract> CommonEncounterSources => _formations.CommonSources;

        internal DungeonRunFloorProgression(LevelRunSession current,
            DungeonRunSharedState shared, List<LevelRunSaveData> completed,
            DungeonRunExplorationState exploration, DungeonRunFormationState formations)
        {
            Current = current ?? throw new ArgumentNullException(nameof(current));
            _shared = shared ?? throw new ArgumentNullException(nameof(shared));
            _exploration = exploration ?? throw new ArgumentNullException(nameof(exploration));
            _formations = formations ?? throw new ArgumentNullException(nameof(formations));
            _completed = completed ?? new List<LevelRunSaveData>();
        }

        internal DungeonRunExplorationSaveData CaptureExploration() => _exploration.Capture();
        internal DungeonRunFormationSaveData CaptureFormations() => _formations.Capture();
        internal DungeonRunEncounterSnapshot ResolveRoster(string scenarioId, string nodeId = null)
            => _formations.ResolveRoster(Current, scenarioId, nodeId);
        internal EnemyFormationPlan ResolveFormation(string scenarioId, IReadOnlyList<EnemyStats> enemies, string nodeId = null)
            => _formations.Resolve(Current, scenarioId, enemies, nodeId);
        internal EnemyFormationPlan MaterializeEnemyEncounter(string scenarioId, string nodeId,
            Func<DungeonRunEncounterSnapshot, List<EnemyStats>> materializeEnemies, out List<EnemyStats> enemies)
        {
            LevelRunSession floor = Current;
            DungeonRunFormationState formations = _formations;
            return formations.Materialize(floor, scenarioId, nodeId, materializeEnemies,
                () => ReferenceEquals(Current, floor) && ReferenceEquals(_formations, formations), out enemies);
        }
        internal List<LevelRunSaveData> CaptureHistory()
        {
            var result = new List<LevelRunSaveData>(_completed.Count);
            foreach (LevelRunSaveData floor in _completed)
                result.Add(JsonUtility.FromJson<LevelRunSaveData>(JsonUtility.ToJson(floor)));
            return result;
        }

        // Historical reward recovery reads the frozen completion proof; it
        // never reopens or mutates a previously visited floor.
        internal bool TryReadCompletedInteractionReceipt(string floorId, string nodeId,
            string effectId, out LevelInteractionReceipt receipt)
        {
            receipt = default;
            var floor = _completed.Find(item => item.profileContentId == floorId);
            if (floor == null || !floor.nodeStates.Exists(item => item.nodeContentId == nodeId &&
                    (item.progress == LevelNodeProgress.Cleared || item.progress == LevelNodeProgress.Available))) return false;
            var saved = floor.interactionReceipts.Find(item => item.effectId == effectId);
            if (saved == null) return false;
            receipt = new LevelInteractionReceipt(saved.effectId, saved.kind, saved.targetContentId,
                saved.presentationId, saved.amount, saved.valueBefore, saved.valueAfter, false,
                saved.summaryText, saved.detailText, saved.rarity, saved.targetRuntimeInstanceId);
            return true;
        }

        private LevelRunSession ForkCurrent(DungeonRunSharedState modifiers = null)
            => Current.ForkForFloorExit(modifiers ?? _shared.CloneForFloorPreparation());

        internal sealed class PreparedAdvance
        {
            internal readonly DungeonRunFloorProgression Owner;
            internal readonly LevelRunSession Previous, Completed, Next;
            internal readonly DungeonRunExplorationState Exploration;
            internal readonly DungeonRunFormationState Formations;
            internal readonly DungeonRunSharedState PreparedModifiers;
            internal readonly DungeonRunSideEventState SideEvents;
            internal PreparedAdvance(DungeonRunFloorProgression owner,
                LevelRunSession previous, LevelRunSession completed, LevelRunSession next,
                DungeonRunExplorationState exploration, DungeonRunFormationState formations, DungeonRunSharedState modifiers, DungeonRunSideEventState sideEvents)
            { SideEvents = sideEvents; Owner = owner; Previous = previous; Completed = completed; Next = next; Exploration = exploration; Formations = formations; PreparedModifiers = modifiers; }
        }

        /// <summary>Read-only admission check: no choice generation, curve evaluation or rewards.</summary>
        internal bool TryValidateNext(Func<string, LevelRunProfile> resolver,
            out LevelRunProfile next, out string error, bool? demo = null)
        {
            next = null;
            error = null;
            try
            {
                // Reject drift before saving this floor into completed history.
                Current.NodeDirectory.RequireMatches(Current.Profile);
                if (_sideEvents == null && Current.NodeDirectory.HasFrozenInteractions)
                    throw new InvalidOperationException("Run side-event source-state recovery must be connected before advancing a frozen floor.");
                _sideEvents?.RequireFloor(Current, requireCompletedPhases: true);
                _formations.RequireFloor(Current.Profile);
                string nextId = Current.Profile.nextFloorContentId;
                if (string.IsNullOrWhiteSpace(nextId))
                    throw new InvalidOperationException("Current floor has no nextFloorContentId.");
                if (string.Equals(nextId, Current.Profile.contentId, StringComparison.OrdinalIgnoreCase) ||
                    _completed.Exists(item => string.Equals(item.profileContentId, nextId, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("A floor transition cannot revisit completed floor history.");
                next = resolver?.Invoke(nextId);
                if (next == null) throw new InvalidOperationException("下一层配置尚未接入：" + nextId);
                if (!string.Equals(next.contentId, nextId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Next-floor resolver returned a different content identity.");
                if (!DemoReleasePolicy.AllowsLevel(next.contentId, next.dungeonContentId,
                        demo ?? DemoReleasePolicy.IsDemoBuild))
                    throw new InvalidOperationException("当前试玩版本不包含下一层：" + next.contentId);
                List<string> errors = next.Validate();
                if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors));
                string snapshotId = next.ResolveDefaultSnapshotId();
                if (string.IsNullOrWhiteSpace(snapshotId))
                    throw new InvalidOperationException("Next floor has no starting snapshot.");
                _formations.ValidatePreparation(next);
                _sideEvents?.ValidateFloorPreparation(next);
                return true;
            }
            catch (Exception exception) { next = null; error = exception.Message; return false; }
        }

        /// <summary>Preflight is pure for this Run, including snapshot effects.</summary>
        internal bool TryPrepare(Func<string, LevelRunProfile> resolver,
            out PreparedAdvance prepared, out string error,
            Action<LevelRunSession> prepareExit = null)
        {
            prepared = null;
            error = null;
            try
            {
                if (!TryValidateNext(resolver, out LevelRunProfile next, out error)) return false;
                string snapshotId = next.ResolveDefaultSnapshotId();
                DungeonRunExplorationState exploration = _exploration.Clone();
                LevelExplorationGuardView guards = exploration.PrepareFloor(next);
                DungeonRunFormationState formations = _formations.Clone();
                formations.PrepareFloor(next);
                DungeonRunSharedState modifiers = _shared.CloneForFloorPreparation();
                LevelRunSession completed = ForkCurrent(modifiers);
                // Exit's local effects precede next-floor snapshot effects, all
                // against the same detached modifiers. No live counters change.
                prepareExit?.Invoke(completed);
                DungeonRunSideEventState sideEvents = _sideEvents?.Clone();
                LevelRunNodeDirectory directory = sideEvents?.PrepareFloor(next, Seed, SideEventContext, Current.RunId,
                    _activeArcanas?.Invoke() ?? Array.Empty<DungeonRunArcanaDefinition>());
                var detached = new LevelRunSession(next, snapshotId, Current.RunId, modifiers,
                    explorationGuards: guards, nodeDirectory: directory);
                // Rebind by restoring pure node state, not by replaying snapshot
                // rewards against the live shared counters during preflight.
                LevelRunSession nextSession = LevelRunSession.Restore(next,
                    detached.CaptureSaveData(), Current.RunId, _shared, guards, directory);
                prepared = new PreparedAdvance(this, Current, completed, nextSession, exploration, formations, modifiers, sideEvents);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        /// <summary>Same-floor source publication preserves the existing session identity.
        /// Strict persistence must publish this exact payload, or throw without publishing.</summary>
        internal void CommitSourcePhase(LevelRunSession prepared, DungeonRunSideEventState sources, Action persist)
        {
            if (persist == null) throw new InvalidOperationException("Source publication requires strict persistence.");
            var previous = Current.CaptureTransactionState();
            var priorSources = _sideEvents.Clone();
            var expectedDirectory = Current.NodeDirectory;
            sources.RequireFloor(prepared);
            try
            {
                Current.AdoptPreparedDirectoryState(prepared, expectedDirectory);
                _sideEvents.Adopt(sources);
                string checkpoint = JsonUtility.ToJson(Current.CaptureSaveData());
                string sourceCheckpoint = _sideEvents.CaptureCanonicalJson();
                persist();
                if (checkpoint != JsonUtility.ToJson(Current.CaptureSaveData()) || sourceCheckpoint != _sideEvents.CaptureCanonicalJson())
                    throw new InvalidOperationException("Persistence must only save the exact source checkpoint.");
            }
            catch
            {
                _sideEvents.Adopt(priorSources);
                Current.AdoptPreparedDirectoryState(previous, Current.NodeDirectory);
                throw;
            }
        }

        internal bool TryReadCompletedExit(string floorId, string nodeId,
            Func<string, LevelRunProfile> resolver, out DungeonRunFloorExitResult result)
        {
            result = null;
            int index = _completed.FindIndex(item => string.Equals(item.profileContentId,
                floorId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            LevelRunSaveData data = _completed[index];
            LevelRunProfile profile = resolver?.Invoke(floorId);
            if (profile == null || !string.Equals(profile.contentId, floorId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Completed-floor resolver returned a different or missing identity.");
            var restored = LevelRunSession.Restore(profile, data, Current.RunId,
                _shared.CloneForFloorPreparation(), _exploration.ReadFloor(profile));
            LevelNode node = restored.FindNode(nodeId);
            if (node?.nodeType != LevelNodeType.Exit || !data.nodeStates.Exists(item =>
                    item.nodeContentId == nodeId && item.progress == LevelNodeProgress.Cleared))
                return false;
            _formations.RequireFloor(profile);
            var receipts = new List<LevelInteractionReceipt>();
            foreach (LevelInteractionEffect effect in node.completionEffects ?? new List<LevelInteractionEffect>())
            {
                if (effect == null || !restored.TryGetInteractionReceipt(effect.effectId, out var receipt))
                    throw new InvalidOperationException("Completed Exit is missing its reward receipt.");
                if (receipt.Kind != effect.kind)
                    throw new InvalidOperationException("Completed Exit receipt kind no longer matches its contract.");
                receipts.Add(receipt);
            }
            string nextId = index + 1 < _completed.Count ? _completed[index + 1].profileContentId : Current.Profile.contentId;
            result = new DungeonRunFloorExitResult(floorId, nodeId, nextId, receipts, false);
            return true;
        }

        /// <summary>
        /// The caller has prepared the completed Exit in a detached same-floor
        /// session. Persist sees the final floor/history/bindings together. A
        /// failure restores references, history, modifiers and old map identity.
        /// The caller owns rollback of its external wallet and Run income.
        /// </summary>
        internal void Commit(PreparedAdvance prepared, Action persist = null)
        {
            if (prepared == null || prepared.Owner != this || prepared.Previous != Current)
                throw new InvalidOperationException("Prepared floor advance is stale.");
            LevelRunSession previous = Current;
            LevelRunSession before = ForkCurrent();
            DungeonRunExplorationState priorExploration = _exploration;
            DungeonRunFormationState priorFormations = _formations;
            DungeonRunSharedState priorModifiers = _shared.CloneForFloorPreparation();
            int priorCount = _completed.Count;
            DungeonRunSideEventState priorSources = _sideEvents?.Clone();
            try
            {
                previous.AdoptPreparedState(prepared.Completed);
                _completed.Add(previous.CaptureSaveData());
                _shared.CopyFloorModifiersFrom(prepared.PreparedModifiers);
                _exploration = prepared.Exploration;
                _formations = prepared.Formations;
                Current = prepared.Next;
                if (prepared.SideEvents != null) _sideEvents.Adopt(prepared.SideEvents);
                string checkpoint = JsonUtility.ToJson(Current.CaptureSaveData());
                string sourceCheckpoint = _sideEvents?.CaptureCanonicalJson();
                persist?.Invoke();
                if (Current != prepared.Next || checkpoint != JsonUtility.ToJson(Current.CaptureSaveData()) ||
                    sourceCheckpoint != _sideEvents?.CaptureCanonicalJson())
                    throw new InvalidOperationException("Persistence must only save the prepared floor/source checkpoint.");
            }
            catch
            {
                Current = previous;
                _exploration = priorExploration;
                _formations = priorFormations;
                if (priorSources != null) _sideEvents.Adopt(priorSources);
                _shared.CopyFloorModifiersFrom(priorModifiers);
                while (_completed.Count > priorCount) _completed.RemoveAt(_completed.Count - 1);
                previous.AdoptPreparedState(before);
                throw;
            }
        }
    }
}
