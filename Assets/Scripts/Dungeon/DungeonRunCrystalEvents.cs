using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>One committed physical hit. ActualExperience is already Growth-adjusted; never scale it again.</summary>
    public readonly struct CrystalExperienceReceipt
    {
        public string FloorContentId { get; }
        public string NodeContentId { get; }
        public string EffectId { get; }
        public string SourceEventId => "PassiveEvent_Crystal";
        public int CardRuntimeInstanceId { get; }
        public int HitOrdinal { get; }
        public int RawExperience { get; }
        public int ActualExperience { get; }
        public int LevelBefore { get; }
        public int LevelAfter { get; }
        public int ExperienceBefore { get; }
        public int ExperienceAfter { get; }
        public bool NewlyApplied { get; }
        internal CrystalExperienceReceipt(DungeonRunCrystalEntry entry, DungeonRunCrystalHitData hit, bool newlyApplied)
        {
            FloorContentId = entry.floorContentId; NodeContentId = entry.nodeContentId; EffectId = entry.effectId;
            CardRuntimeInstanceId = hit.cardRuntimeInstanceId; HitOrdinal = hit.ordinal;
            RawExperience = hit.rawExperience; ActualExperience = hit.actualExperience;
            LevelBefore = hit.levelBefore; LevelAfter = hit.levelAfter;
            ExperienceBefore = hit.experienceBefore; ExperienceAfter = hit.experienceAfter; NewlyApplied = newlyApplied;
        }
    }

    /// <summary>Ephemeral capability accepted only by the same owner's current synchronous commit.</summary>
    internal sealed class DungeonRunCrystalExperienceCommand
    {
        internal DungeonRunCrystalEvents Owner { get; }
        internal string NodeContentId { get; }
        internal LevelInteractionEffect Effect { get; }
        internal int CardRuntimeInstanceId { get; }
        internal int HitOrdinal { get; }
        internal int RawExperience { get; }
        internal int AdjustedExperience { get; }
        internal DungeonRunCrystalExperienceCommand(DungeonRunCrystalEvents owner, string nodeId,
            LevelInteractionEffect effect, int physicalId, int ordinal, int raw, int adjusted)
        {
            Owner = owner; NodeContentId = nodeId; Effect = effect; CardRuntimeInstanceId = physicalId;
            HitOrdinal = ordinal; RawExperience = raw; AdjustedExperience = adjusted;
        }
    }

    /// <summary>
    /// Owns Crystal's durable timer, physical throwing membership and hit journal. Interactions remains
    /// the sole XP/natural-reward checkpoint owner. A frame never invents a hit or executes a card.
    /// The approved project adapter captures the whole persistent Run roster and preserves each-combat
    /// regrouping. Per-hit seeded RNG, paused time and partial-event restoration are project guarantees,
    /// not proof of VC's seed stream or partial saves. Experience collections are owned by the Run ledger; source placement stays with SideEvents.
    /// Scale review: the DTO, strict wire and restored identity checks describe this one lifecycle; they
    /// share its sole state. Before adding another event or account metric, reassess this boundary.
    /// </summary>
    public sealed class DungeonRunCrystalEvents
    {
        private const int Version = 1;
        private const string SourceId = "PassiveEvent_Crystal";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly DungeonRunSession _run;
        private DungeonRunCrystalSaveData _state;
        private DungeonRunCrystalExperienceCommand _preparedCommand;
        internal bool IsCommitting { get; private set; }
        public bool HasPendingEvent => _state.entries.Any(e => !e.finished);
        public string PendingNodeContentId => _state.entries.FirstOrDefault(e => !e.finished)?.nodeContentId;
        public long TotalExperienceCollected => SumExperience(_state.entries);
        public long GetFloorExperienceCollected(string floorId)
            => SumExperience(_state.entries.Where(e => e.floorContentId == floorId));
        public IReadOnlyList<CrystalExperienceReceipt> CaptureHitReceipts()
            => Array.AsReadOnly(_state.entries.SelectMany(e => e.hits.Select(h =>
                new CrystalExperienceReceipt(e, h, false))).ToArray());

        internal DungeonRunCrystalEvents(DungeonRunSession run, DungeonRunCrystalSaveData saved = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _state = saved == null ? Empty() : Clone(saved);
            ValidateLocal(_state, _run.CombatProgression);
            // Other owners are not constructed yet. Full visited-floor and reward proof checks follow assembly.
            var pending = _state.entries.SingleOrDefault(e => !e.finished);
            if (pending != null)
            {
                if (pending.floorContentId != _run.CurrentFloorContentId) throw Invalid("Pending Crystal belongs to another floor.");
                RequireFloorEntry(_run.CurrentFloor, pending);
                RequireRoster(pending);
            }
        }

        /// <summary>Freeze the actual activated native stage, never an invented project floor index.</summary>
        public bool TryBeginSource(string nodeId, Action persist, out string error)
        {
            error = null;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId);
                if (!_run.SideEvents.TryGetCrystalSourceContext(nodeId, effect.effectId, out var context, out error))
                    return false;
                return TryBegin(nodeId, context.CompletedSourceFloors, context.SourceFloorCount, persist, out error);
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        /// <summary>Detached lifecycle values. Inspection never starts, saves, throws or acquires XP.</summary>
        public bool TryInspect(string nodeId, out CrystalRunOffer offer, out string error)
        {
            offer = null; error = null;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId);
                var entry = Find(nodeId);
                if (entry == null) throw Invalid("Crystal has no frozen setup.");
                RequireCheckpoint(entry, effect);
                offer = new CrystalRunOffer(nodeId, RestoreCore(entry, effect).Capture(),
                    Float(entry.exitElapsedBits), entry.completedSourceFloors, entry.sourceFloorCount,
                    SumExperience(new[] { entry }));
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        public bool TryBegin(string nodeId, int completedSourceFloors, int sourceFloorCount, Action persist, out string error)
        {
            if (!Guard(nodeId, false, out error)) return false;
            try
            {
                RequireContext(completedSourceFloors, sourceFloorCount);
                var effect = RequireEffect(_run.CurrentFloor, nodeId);
                var prior = Find(nodeId);
                if (prior != null)
                {
                    RequireCheckpoint(prior, effect);
                    if (prior.completedSourceFloors != completedSourceFloors || prior.sourceFloorCount != sourceFloorCount)
                        throw Invalid("Crystal source-floor context changed from its frozen setup.");
                    if (!prior.finished && _run.CurrentFloor.ActiveInteractionNodeId != nodeId)
                    {
                        RequireAvailable(nodeId);
                        var resumedState = Clone(_state);
                        var resumedEntry = Find(resumedState, nodeId);
                        Commit(resumedState, resumedEntry, effect, () => EnsureActive(nodeId), persist);
                    }
                    return true;
                }
                if (_run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out _))
                    throw Invalid("Crystal floor receipt has no lifecycle proof.");
                RequireAvailable(nodeId);
                var roster = _run.CaptureInteractionDraft().Cards.Select(card => card.runtimeInstanceId).ToArray();
                var core = new CrystalThrowingSession(CrystalEventContracts.GetSpecification(effect), roster);
                var next = Clone(_state);
                var entry = new DungeonRunCrystalEntry
                {
                    floorContentId = _run.CurrentFloorContentId, nodeContentId = nodeId,
                    effectId = effect.effectId, sourceEventId = SourceId,
                    completedSourceFloors = completedSourceFloors, sourceFloorCount = sourceFloorCount,
                    initialLevel = _run.CombatProgression.CurrentLevel,
                    initialExperience = _run.CombatProgression.CurrentExperience,
                    initialCarryUnits = _run.CombatProgression.XpFractionCarryUnits
                };
                StoreCore(entry, core.Capture()); next.entries.Add(entry);
                Commit(next, entry, effect, () => EnsureActive(nodeId), persist);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool TryStart(string nodeId, Action persist, out string error)
        {
            if (!Guard(nodeId, true, out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId); var previous = RequirePending(nodeId, effect);
                if (previous.stage == (int)CrystalThrowingStage.Throwing) return true;
                var next = Clone(_state); var entry = Find(next, nodeId);
                var core = RestoreCore(entry, effect); core.StartThrowing(); StoreCore(entry, core.Capture());
                Commit(next, entry, effect, () => EnsureActive(nodeId), persist); return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool TryAdvanceFrame(string nodeId, float delta, bool paused, bool pressed, bool released,
            bool throwAll, Action persist, out IReadOnlyList<CrystalCardThrow> throws, out string error)
        {
            throws = Array.Empty<CrystalCardThrow>();
            if (!Guard(nodeId, true, out error)) return false;
            try
            {
                if (!Finite(delta) || delta < 0) throw Invalid("Crystal frame time must be finite and nonnegative.");
                var effect = RequireEffect(_run.CurrentFloor, nodeId); var previous = RequirePending(nodeId, effect);
                if (previous.stage == (int)CrystalThrowingStage.Preparing)
                    throw Invalid("Crystal setup must start before frame input.");
                if (paused) return true;
                var next = Clone(_state); var entry = Find(next, nodeId); var core = RestoreCore(entry, effect);
                IReadOnlyList<CrystalCardThrow> preparedThrows = Array.Empty<CrystalCardThrow>();
                if (core.Stage == CrystalThrowingStage.Ending)
                {
                    float exit = (float)((double)Float(entry.exitElapsedBits) + delta);
                    if (!Finite(exit)) throw Invalid("Crystal exit time overflow.");
                    entry.exitElapsedBits = Bits(Math.Min(exit, CrystalEventContracts.GetSpecification(effect).Timing.ExitTime));
                }
                else preparedThrows = core.AdvanceFrame(delta, false, pressed, released, throwAll);
                StoreCore(entry, core.Capture());
                if (EntryFingerprint(previous) == EntryFingerprint(entry)) return true;
                Commit(next, entry, effect, () => EnsureActive(nodeId), persist);
                throws = Array.AsReadOnly(preparedThrows.ToArray()); return true;
            }
            catch (Exception ex) { throws = Array.Empty<CrystalCardThrow>(); error = ex.Message; return false; }
        }

        public bool TryHit(string nodeId, int physicalId, DungeonRunInteractionCatalog catalog, Action persist,
            out CrystalExperienceReceipt receipt, out string error)
        {
            receipt = default;
            // Saved hit replay precedes choice/stage checks, but never the aggregate transaction guard.
            if (!Guard(nodeId, false, out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId); var previous = Find(nodeId);
                if (previous == null) throw Invalid("Crystal has no frozen setup.");
                RequireCheckpoint(previous, effect);
                var oldHit = previous.hits.Find(h => h.cardRuntimeInstanceId == physicalId);
                if (oldHit != null) { receipt = new CrystalExperienceReceipt(previous, oldHit, false); return true; }
                RequireNoRewardChoice();
                if (previous.finished || catalog == null) throw Invalid("Crystal hit requires an active event and reward catalog.");
                var next = Clone(_state); var entry = Find(next, nodeId); var core = RestoreCore(entry, effect);
                if (!core.TryRegisterHit(physicalId, out bool isNew) || !isNew)
                    throw Invalid("Only a currently flying physical card can register a new Crystal hit.");
                StoreCore(entry, core.Capture());
                int ordinal = entry.hits.Count + 1;
                var source = CrystalEventContracts.GetSpecification(effect);
                int raw = CrystalThrowingSession.DrawRawHitExperience(source, entry.completedSourceFloors,
                    entry.sourceFloorCount, new SeededRandomSource(HitSeed(_run.RunId, effect.effectId, physicalId)));
                int actual = CrystalThrowingSession.CalculateSourceAdjustedHitExperience(raw, catalog.GrowthPercent);
                var hit = new DungeonRunCrystalHitData
                {
                    cardRuntimeInstanceId = physicalId, ordinal = ordinal, rawExperience = raw,
                    actualExperience = actual, growthPercent = catalog.GrowthPercent,
                    levelBefore = _run.CombatProgression.CurrentLevel,
                    experienceBefore = _run.CombatProgression.CurrentExperience,
                    carryUnits = _run.CombatProgression.XpFractionCarryUnits,
                    requiredExperienceBefore = _run.CombatProgression.GetRequiredExperience(_run.CombatProgression.CurrentLevel)
                };
                entry.hits.Add(hit);
                var command = new DungeonRunCrystalExperienceCommand(this, nodeId, effect, physicalId, ordinal, raw, actual);
                var oldRewards = _run.Interactions.CaptureSaveData();
                try
                {
                    Commit(next, entry, effect, () =>
                    {
                        EnsureActive(nodeId); _preparedCommand = command;
                        _run.Interactions.StageCrystalHit(command, catalog);
                        hit.levelAfter = _run.CombatProgression.CurrentLevel;
                        hit.experienceAfter = _run.CombatProgression.CurrentExperience;
                        if (_run.CombatProgression.XpFractionCarryUnits != hit.carryUnits)
                            throw Invalid("Already adjusted Crystal XP cannot modify the fractional carry ledger.");
                    }, persist);
                }
                catch { _run.Interactions.RestoreCrystalCheckpoint(oldRewards); throw; }
                finally { _preparedCommand = null; }
                receipt = new CrystalExperienceReceipt(entry, hit, true); return true;
            }
            catch (Exception ex) { receipt = default; error = ex.Message; return false; }
        }

        public bool TryFinish(string nodeId, Action persist, out LevelInteractionReceipt receipt, out string error)
        {
            receipt = default;
            if (!Guard(nodeId, false, out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId); var previous = Find(nodeId);
                if (previous == null) throw Invalid("Crystal has no frozen setup.");
                RequireCheckpoint(previous, effect);
                if (previous.finished)
                { _run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out receipt); return true; }
                RequireNoRewardChoice();
                var next = Clone(_state); var entry = Find(next, nodeId); var core = RestoreCore(entry, effect);
                if (core.Stage != CrystalThrowingStage.Ending ||
                    Float(entry.exitElapsedBits) < CrystalEventContracts.GetSpecification(effect).Timing.ExitTime)
                    throw Invalid("Crystal throwing and exit time must finish before cleanup.");
                core.ReturnCards(); StoreCore(entry, core.Capture()); entry.finished = true;
                entry.finalLevel = _run.CombatProgression.CurrentLevel;
                entry.finalExperience = _run.CombatProgression.CurrentExperience;
                entry.finalCarryUnits = _run.CombatProgression.XpFractionCarryUnits;
                LevelInteractionReceipt applied = default; int total = checked((int)SumExperience(new[] { entry }));
                Commit(next, entry, effect, () =>
                {
                    EnsureActive(nodeId);
                    if (!_run.CurrentFloor.TryRecordExternalEffect(nodeId, effect, 0, total, total, out applied, out string failure) ||
                        !_run.CurrentFloor.TryCompleteInteraction(nodeId, out failure)) throw Invalid(failure);
                }, persist);
                receipt = applied; return true;
            }
            catch (Exception ex) { receipt = default; error = ex.Message; return false; }
        }

        private void Commit(DungeonRunCrystalSaveData next, DungeonRunCrystalEntry entry,
            LevelInteractionEffect effect, Action apply, Action persist)
        {
            if (persist == null) throw Invalid("Strict atomic persistence is required before Crystal mutation.");
            var previous = _state; var floor = _run.CurrentFloor;
            IsCommitting = true;
            try
            {
                _run.CommitRewardState(() =>
                {
                    _state = next; apply(); next.fingerprint = Fingerprint(next.entries);
                    ValidateLocal(next, _run.CombatProgression); RequireCheckpoint(entry, effect); RequireRewardCheckpoint();
                }, () =>
                {
                    int level = _run.CombatProgression.CurrentLevel, xp = _run.CombatProgression.CurrentExperience,
                        carry = _run.CombatProgression.XpFractionCarryUnits, cardCount = _run.RunCardCount,
                        health = _run.CurrentHealth, maximum = _run.MaxHealth;
                    persist();
                    if (!ReferenceEquals(floor, _run.CurrentFloor) || !ReferenceEquals(_state, next) ||
                        !floor.MatchesConfiguredEffect(entry.nodeContentId, effect) ||
                        level != _run.CombatProgression.CurrentLevel || xp != _run.CombatProgression.CurrentExperience ||
                        carry != _run.CombatProgression.XpFractionCarryUnits || cardCount != _run.RunCardCount ||
                        health != _run.CurrentHealth || maximum != _run.MaxHealth)
                        throw Invalid("Crystal floor, owner or canonical effect changed during persistence.");
                    ValidateLocal(next, _run.CombatProgression); RequireCheckpoint(entry, effect); RequireRewardCheckpoint();
                });
            }
            catch { _state = previous; throw; }
            finally { IsCommitting = false; }
        }

        private bool Guard(string nodeId, bool rejectChoice, out string error)
        {
            error = null;
            if (IsCommitting || _run.IsRewardCommitInProgress)
            { error = "Crystal transaction is already committing."; return false; }
            if (_run.TryGetConclusion(out _)) { error = "A concluded Run cannot operate Crystal."; return false; }
            if (HasPendingEvent && PendingNodeContentId != nodeId)
            { error = "Another Crystal event is pending."; return false; }
            if (_run.Arcanas.HasPendingChoice || (_run.CardOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy) ||
                (_run.Interactions.HasPendingChoice && (rejectChoice || _run.Interactions.PendingNodeContentId != nodeId)))
            { error = "Complete the current Run choice first."; return false; }
            var floor = _run.CurrentFloor;
            if (floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _) ||
                (!string.IsNullOrEmpty(floor.ActiveInteractionNodeId) && floor.ActiveInteractionNodeId != nodeId))
            { error = "Crystal requires its own idle map interaction."; return false; }
            return true;
        }

        private void RequireNoRewardChoice()
        { if (_run.Interactions.HasPendingChoice) throw Invalid("Resolve the Crystal natural-level reward first."); }

        private DungeonRunCrystalEntry RequirePending(string nodeId, LevelInteractionEffect effect)
        {
            var entry = Find(nodeId);
            if (entry == null || entry.finished) throw Invalid("Crystal has no active frozen setup.");
            RequireCheckpoint(entry, effect); return entry;
        }

        private void RequireAvailable(string nodeId)
        {
            if (_run.CurrentFloor.GetNodeState(nodeId) != LevelNodeViewState.Available &&
                !(_run.CurrentFloor.GetNodeState(nodeId) == LevelNodeViewState.Completing &&
                  _run.CurrentFloor.ActiveInteractionNodeId == nodeId))
                throw Invalid("Crystal node is not available.");
        }

        private void EnsureActive(string nodeId)
        {
            if (!_run.CurrentFloor.TryBeginInteraction(nodeId, out string error)) throw Invalid(error);
        }

        private static LevelInteractionEffect RequireEffect(LevelRunSession floor, string nodeId)
        {
            var node = floor.FindNode(nodeId);
            var effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
            var errors = new List<string>(); CrystalEventContracts.ValidateEffect(effect, errors);
            if (node?.nodeType != LevelNodeType.Interaction || errors.Count != 0)
                throw Invalid("Node is not a canonical single Crystal interaction.");
            CrystalEventContracts.GetSpecification(effect); return effect;
        }

        private void RequireCheckpoint(DungeonRunCrystalEntry entry, LevelInteractionEffect effect)
        {
            if (entry.floorContentId != _run.CurrentFloorContentId || entry.effectId != effect.effectId ||
                entry.sourceEventId != SourceId) throw Invalid("Crystal identity changed from its frozen checkpoint.");
            RequireFloorEntry(_run.CurrentFloor, entry); if (!entry.finished) RequireRoster(entry);
        }

        private static void RequireFloorEntry(LevelRunSession floor, DungeonRunCrystalEntry entry)
        {
            var effect = RequireEffect(floor, entry.nodeContentId);
            if (entry.floorContentId != floor.Profile.contentId || entry.effectId != effect.effectId)
                throw Invalid("Crystal journal is not bound to its exact floor effect.");
            RestoreCore(entry, effect);
            bool has = floor.TryGetInteractionReceipt(effect.effectId, out var receipt);
            var state = floor.GetNodeState(entry.nodeContentId);
            if (entry.finished)
            {
                int total = checked((int)SumExperience(new[] { entry }));
                if (state != LevelNodeViewState.Completed || !has || receipt.Kind != effect.kind ||
                    receipt.ValueBefore != 0 || receipt.ValueAfter != total || receipt.Amount != total ||
                    receipt.TargetRuntimeInstanceId != 0 || !EqualOptional(receipt.TargetContentId, effect.targetContentId) ||
                    !EqualOptional(receipt.PresentationId, effect.presentationId) || !EqualOptional(receipt.SummaryText, effect.summaryText) ||
                    !EqualOptional(receipt.DetailText, effect.detailText) || receipt.Rarity != effect.rarity)
                    throw Invalid("Finished Crystal lacks its exact terminal floor receipt.");
            }
            else if (has || (state != LevelNodeViewState.Available && state != LevelNodeViewState.Completing) ||
                (state == LevelNodeViewState.Completing ? floor.ActiveInteractionNodeId != entry.nodeContentId :
                    !string.IsNullOrEmpty(floor.ActiveInteractionNodeId)))
                throw Invalid("Pending Crystal floor progress disagrees with its lifecycle.");
            if (!entry.finished && (floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _)))
                throw Invalid("Crystal checkpoint cannot coexist with combat or a pending return.");
        }

        private void RequireRoster(DungeonRunCrystalEntry entry)
        {
            var ids = new HashSet<int>(_run.CaptureInteractionDraft().Cards.Select(card => card.runtimeInstanceId));
            if (entry.rosterIds.Any(id => !ids.Contains(id)))
                throw Invalid("Captured Crystal physical cards are missing from the persistent Run roster.");
        }

        internal void RequirePreparedCommand(DungeonRunCrystalExperienceCommand command)
        {
            if (!IsCommitting || command == null || !ReferenceEquals(command, _preparedCommand) ||
                !ReferenceEquals(command.Owner, this) || !ReferenceEquals(_run.Crystals, this))
                throw Invalid("Crystal XP requires its owner's current prepared command capability.");
            var entry = Find(command.NodeContentId);
            var hit = entry?.hits.LastOrDefault();
            if (entry == null || entry.finished || hit == null || hit.cardRuntimeInstanceId != command.CardRuntimeInstanceId ||
                hit.ordinal != command.HitOrdinal || hit.rawExperience != command.RawExperience ||
                hit.actualExperience != command.AdjustedExperience || entry.effectId != command.Effect.effectId ||
                !_run.CurrentFloor.MatchesConfiguredEffect(command.NodeContentId, command.Effect))
                throw Invalid("Prepared Crystal command disagrees with its physical hit proof.");
        }

        internal bool TryGetCommittedHit(string effectId, int physicalId, out CrystalExperienceReceipt receipt)
        {
            receipt = default;
            var entry = _state.entries.SingleOrDefault(e => !e.finished && e.floorContentId == _run.CurrentFloorContentId && e.effectId == effectId);
            var hit = entry?.hits.Find(h => h.cardRuntimeInstanceId == physicalId);
            if (hit == null) return false;
            receipt = new CrystalExperienceReceipt(entry, hit, false); return true;
        }

        internal void RequireRewardCheckpoint()
        {
            _run.Interactions.RequireCrystalRewardCheckpoint(this);
            var pending = _state.entries.SingleOrDefault(e => !e.finished);
            if (pending == null) return;
            if (_run.TryGetConclusion(out _) || _run.IsDungeonBonusGranted || _run.IsLevelBonusGranted)
                throw Invalid("A pending Crystal event cannot restore a conclusion or terminal reward.");
            var last = pending.hits.LastOrDefault();
            int level = last?.levelAfter ?? pending.initialLevel;
            int xp = last?.experienceAfter ?? pending.initialExperience;
            int carry = last?.carryUnits ?? pending.initialCarryUnits;
            RequireProgressionTransition(_run.CombatProgression, level, xp,
                _run.CombatProgression.CurrentLevel, _run.CombatProgression.CurrentExperience,
                requireSettled: last != null && !_run.Interactions.HasPendingChoice);
            if (_run.CombatProgression.XpFractionCarryUnits != carry)
                throw Invalid("Crystal pending progression changed its fractional carry.");
            if (last == null && (_run.CombatProgression.CurrentLevel != level || _run.CombatProgression.CurrentExperience != xp))
                throw Invalid("Unhit Crystal setup cannot advance player progression.");
        }

        internal void RequireVisitedFloors(IReadOnlyList<LevelRunSession> visited)
        {
            if (visited == null) throw Invalid("Crystal restore requires visited floors.");
            var floors = new Dictionary<string, LevelRunSession>(StringComparer.Ordinal);
            foreach (var floor in visited)
                if (floor == null || floors.ContainsKey(floor.Profile.contentId)) throw Invalid("Visited Crystal floor identities are invalid.");
                else floors.Add(floor.Profile.contentId, floor);
            foreach (var entry in _state.entries)
            {
                if (!floors.TryGetValue(entry.floorContentId, out var floor)) throw Invalid("Crystal proof belongs to an unvisited floor.");
                if (!entry.finished && entry.floorContentId != _run.CurrentFloorContentId)
                    throw Invalid("A historical floor cannot retain a pending Crystal event.");
                RequireFloorEntry(floor, entry);
            }
            foreach (var floor in floors.Values)
            foreach (var node in floor.Nodes)
            {
                if (node == null || !floor.IsNodeEnabled(node.contentId)) continue;
                if (node?.completionEffects?.Any(e => e?.kind == LevelInteractionEffectKind.CrystalThrowing) != true) continue;
                var effect = RequireEffect(floor, node.contentId);
                var entry = _state.entries.SingleOrDefault(e => e.floorContentId == floor.Profile.contentId && e.nodeContentId == node.contentId);
                bool receipt = floor.TryGetInteractionReceipt(effect.effectId, out _);
                var progress = floor.GetNodeState(node.contentId);
                if ((receipt || progress == LevelNodeViewState.Completed || progress == LevelNodeViewState.Completing) && entry == null)
                    throw Invalid("Crystal floor progress has no owner proof.");
            }
        }

        internal DungeonRunCrystalSaveData CaptureSaveData() => Clone(_state);

        /// <summary>All owner fields, including legal zero values and arrays, must exist before Unity materializes DTOs.</summary>
        internal static DungeonRunCrystalSaveData ReadSaveData(string rawJson)
        {
            RequireWire(rawJson, typeof(DungeonRunCrystalSaveData));
            var result = JsonUtility.FromJson<DungeonRunCrystalSaveData>(rawJson);
            // Run identity/native source payout checks happen in the real owner constructor.
            if (result == null || result.version != Version || result.entries == null ||
                !Stable(result.fingerprint) || result.fingerprint != Fingerprint(result.entries))
                throw Invalid("Crystal owner wire version or fingerprint is invalid.");
            return Clone(result);
        }

        internal static void RequireRunEnvelope(string payload, bool modern, bool experienceModern, bool gemModern, bool additionalStatsModern)
        {
            var names = ReadFieldNames(payload);
            string[] additionalNames = { "hasAdditionalStatState", "additionalStats", "hasRevivalReserve", "revivalReserve" };
            foreach (string name in additionalNames)
            {
                if (names.Any(actual => actual.Equals(name, StringComparison.OrdinalIgnoreCase) && actual != name))
                    throw Invalid("Run additional-stat aliases are not canonical.");
                if (additionalStatsModern != names.Contains(name))
                    throw Invalid(additionalStatsModern ? "Run requires explicit additional-stat owner fields." : "Legacy Run cannot carry future additional-stat fields.");
            }
            if (names.Any(name => name.Equals("hasGemChestState", StringComparison.OrdinalIgnoreCase) && name != "hasGemChestState" ||
                                  name.Equals("gemChestStateJson", StringComparison.OrdinalIgnoreCase) && name != "gemChestStateJson"))
                throw Invalid("Run Gem Chest field aliases are not canonical.");
            bool gemFlag = names.Contains("hasGemChestState"), gemBody = names.Contains("gemChestStateJson");
            if (gemModern ? !gemFlag || !gemBody : gemFlag || gemBody)
                throw Invalid(gemModern ? "Run requires explicit Gem Chest owner fields." : "Legacy Run cannot carry future Gem Chest fields.");
            bool xpFlag = names.Contains("hasExperienceCollectionsState"), xpBody = names.Contains("experienceCollections");
            if (experienceModern ? !xpFlag || !xpBody : xpFlag || xpBody)
                throw Invalid(experienceModern ? "Run requires explicit XP collection owner fields." : "Legacy Run cannot carry future XP fields.");
            bool flag = names.Contains("hasCrystalState"), body = names.Contains("crystalEvents");
            if (modern ? !flag || !body : flag || body)
                throw Invalid(modern ? "Run requires explicit Crystal owner fields." : "Legacy Run cannot carry future Crystal fields.");
        }

        private static void RequireWire(string raw, Type type)
        {
            if (type == typeof(int))
            {
                if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ||
                    raw.Contains(".") || raw.Contains("e") || raw.Contains("E") || raw.StartsWith("+", StringComparison.Ordinal))
                    throw Invalid("Crystal wire integer is malformed.");
                return;
            }
            if (type == typeof(bool))
            { if (raw != "true" && raw != "false") throw Invalid("Crystal wire boolean is malformed."); return; }
            if (type == typeof(string))
            { if (raw == null || ReadStringEnd(raw, 0) != raw.Length) throw Invalid("Crystal wire string must be explicit."); return; }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!JsonObjectShape.TryGetArrayItems(raw, out var items)) throw Invalid("Crystal wire array is missing or null.");
                foreach (string item in items) RequireWire(item, type.GetGenericArguments()[0]); return;
            }
            if (type != typeof(DungeonRunCrystalSaveData) && type != typeof(DungeonRunCrystalEntry) && type != typeof(DungeonRunCrystalHitData))
                throw Invalid("Unexpected Crystal wire DTO type.");
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            var names = ReadFieldNames(raw);
            if (names.Count != fields.Length || names.Any(name => !fields.Any(field => field.Name == name)))
                throw Invalid("Crystal wire object contains unknown or duplicated fields.");
            foreach (var field in fields)
            {
                if (!JsonObjectShape.TryGetField(raw, field.Name, out string value))
                    throw Invalid("Crystal wire field is missing or duplicated: " + field.Name);
                RequireWire(value, field.FieldType);
            }
        }

        // This small parser validates this owner's closed DTO envelope; Core's scanner still locates values/arrays.
        internal static HashSet<string> ReadFieldNames(string raw)
        {
            int index = 0; SkipSpace(raw, ref index);
            if (index >= raw.Length || raw[index++] != '{') throw Invalid("Crystal wire object is missing.");
            var names = new HashSet<string>(StringComparer.Ordinal); SkipSpace(raw, ref index);
            if (index < raw.Length && raw[index] == '}') { index++; SkipSpace(raw, ref index); if (index != raw.Length) throw Invalid("Extra Crystal JSON."); return names; }
            while (index < raw.Length)
            {
                int start = index; int end = ReadStringEnd(raw, start);
                string name = raw.Substring(start + 1, end - start - 2);
                if (name.Contains("\\") || !names.Add(name)) throw Invalid("Crystal wire field is escaped or duplicated.");
                index = end; SkipSpace(raw, ref index);
                if (index >= raw.Length || raw[index++] != ':') throw Invalid("Crystal wire colon is missing.");
                SkipSpace(raw, ref index); SkipValue(raw, ref index); SkipSpace(raw, ref index);
                if (index >= raw.Length) throw Invalid("Crystal wire object is incomplete.");
                if (raw[index] == '}') { index++; SkipSpace(raw, ref index); if (index != raw.Length) throw Invalid("Extra Crystal JSON."); return names; }
                if (raw[index++] != ',') throw Invalid("Crystal wire comma is missing."); SkipSpace(raw, ref index);
            }
            throw Invalid("Crystal wire object is incomplete.");
        }
        private static int ReadStringEnd(string raw, int index)
        {
            if (index >= raw.Length || raw[index++] != '"') throw Invalid("Crystal wire string is missing.");
            while (index < raw.Length)
            {
                char c = raw[index++]; if (c == '"') return index;
                if (c < ' ') throw Invalid("Crystal wire string contains an unescaped control character.");
                if (c != '\\') continue;
                if (index >= raw.Length) break; char escape = raw[index++];
                if (escape == 'u')
                {
                    if (index + 4 > raw.Length) break;
                    for (int j = 0; j < 4; j++) if (!Uri.IsHexDigit(raw[index++])) throw Invalid("Crystal wire Unicode escape is malformed.");
                }
                else if ("\"\\/bfnrt".IndexOf(escape) < 0) throw Invalid("Crystal wire escape is malformed.");
            }
            throw Invalid("Crystal wire string is incomplete.");
        }
        private static void SkipValue(string raw, ref int index)
        {
            if (index >= raw.Length) throw Invalid("Crystal wire value is missing.");
            if (raw[index] == '"') { index = ReadStringEnd(raw, index); return; }
            if (raw[index] == '[' || raw[index] == '{')
            {
                var closing = new Stack<char>(); closing.Push(raw[index++] == '[' ? ']' : '}');
                while (index < raw.Length && closing.Count > 0)
                {
                    char c = raw[index];
                    if (c == '"') { index = ReadStringEnd(raw, index); continue; }
                    index++;
                    if (c == '[' || c == '{') closing.Push(c == '[' ? ']' : '}');
                    else if ((c == ']' || c == '}') && c != closing.Pop()) throw Invalid("Crystal wire brackets mismatch.");
                }
                if (closing.Count != 0) throw Invalid("Crystal wire brackets are incomplete."); return;
            }
            int begin = index;
            while (index < raw.Length && !char.IsWhiteSpace(raw[index]) && raw[index] != ',' && raw[index] != '}' && raw[index] != ']') index++;
            if (index == begin) throw Invalid("Crystal wire value is missing.");
        }
        private static void SkipSpace(string raw, ref int index)
        { if (raw == null) throw Invalid("Crystal JSON is missing."); while (index < raw.Length && char.IsWhiteSpace(raw[index])) index++; }

        private static void ValidateLocal(DungeonRunCrystalSaveData state, CombatProgressionState progression)
        {
            if (state == null || state.version != Version || state.entries == null)
                throw Invalid("Crystal save version or entries are missing.");
            var nodes = new HashSet<(string Floor, string Node)>(); var effects = new HashSet<(string Floor, string Effect)>();
            int pending = 0;
            foreach (var entry in state.entries)
            {
                if (entry == null || !Stable(entry.floorContentId) || !Stable(entry.nodeContentId) || !Stable(entry.effectId) ||
                    entry.sourceEventId != SourceId || !nodes.Add((entry.floorContentId, entry.nodeContentId)) || !effects.Add((entry.floorContentId, entry.effectId)) ||
                    entry.rosterIds == null || entry.hits == null) throw Invalid("Crystal identities or collections are invalid or duplicated.");
                RequireContext(entry.completedSourceFloors, entry.sourceFloorCount);
                if (entry.initialLevel < 1 || entry.initialExperience < 0 || entry.initialCarryUnits < 0 ||
                    entry.initialCarryUnits >= CombatProgressionState.ExperienceCarryScale ||
                    (!entry.finished && (entry.finalLevel != 0 || entry.finalExperience != 0 || entry.finalCarryUnits != 0)))
                    throw Invalid("Crystal initial or final progression proof is invalid.");
                var source = CanonicalSpecification();
                RestoreCore(entry, source);
                float exit = Float(entry.exitElapsedBits);
                if (!Finite(exit) || exit < 0 || exit > source.Timing.ExitTime ||
                    (entry.stage < (int)CrystalThrowingStage.Ending && exit != 0) ||
                    entry.finished != (entry.stage == (int)CrystalThrowingStage.Returned) ||
                    (entry.finished && (Float(entry.eventElapsedBits) != source.Timing.EventTime || exit != source.Timing.ExitTime)))
                    throw Invalid("Crystal finish and exit timing proof is invalid.");
                if (!entry.finished && ++pending > 1) throw Invalid("Only one Crystal event may be pending.");
                for (int i = 0; i < entry.hits.Count; i++)
                {
                    var hit = entry.hits[i];
                    if (hit == null || hit.ordinal != i + 1 || hit.cardRuntimeInstanceId <= 0 ||
                        !ValidRawExperience(source, entry, hit.rawExperience) ||
                        hit.actualExperience != CrystalThrowingSession.CalculateSourceAdjustedHitExperience(hit.rawExperience, hit.growthPercent) ||
                        hit.levelBefore < 1 || hit.levelAfter < hit.levelBefore || hit.experienceBefore < 0 || hit.experienceAfter < 0 ||
                        hit.carryUnits < 0 || hit.carryUnits >= CombatProgressionState.ExperienceCarryScale)
                        throw Invalid("Crystal physical-hit payout or progression proof is invalid.");
                    int beforeLevel = i == 0 ? entry.initialLevel : entry.hits[i - 1].levelAfter;
                    int beforeXp = i == 0 ? entry.initialExperience : entry.hits[i - 1].experienceAfter;
                    int beforeCarry = i == 0 ? entry.initialCarryUnits : entry.hits[i - 1].carryUnits;
                    if (i == 0 && (hit.levelBefore != beforeLevel || hit.experienceBefore != beforeXp))
                        throw Invalid("First Crystal hit does not start at its frozen progression.");
                    RequireProgressionTransition(progression, beforeLevel, beforeXp, hit.levelBefore,
                        hit.experienceBefore, requireSettled: i > 0);
                    if (hit.carryUnits != beforeCarry || hit.requiredExperienceBefore != progression.GetRequiredExperience(hit.levelBefore))
                        throw Invalid("Crystal hit threshold or carry changed from the real progression rules.");
                    long available = (long)hit.experienceBefore + hit.actualExperience;
                    bool advances = available >= hit.requiredExperienceBefore;
                    if (available > int.MaxValue || hit.levelAfter != (long)hit.levelBefore + (advances ? 1 : 0) ||
                        hit.experienceAfter != available - (advances ? hit.requiredExperienceBefore : 0))
                        throw Invalid("Crystal first-stage XP and level arithmetic is inconsistent.");
                }
                if (entry.finished)
                {
                    var last = entry.hits.LastOrDefault();
                    RequireProgressionTransition(progression, last?.levelAfter ?? entry.initialLevel,
                        last?.experienceAfter ?? entry.initialExperience, entry.finalLevel, entry.finalExperience,
                        requireSettled: last != null);
                    if (entry.finalCarryUnits != (last?.carryUnits ?? entry.initialCarryUnits))
                        throw Invalid("Crystal final progression carry changed.");
                    if (last == null && (entry.finalLevel != entry.initialLevel || entry.finalExperience != entry.initialExperience))
                        throw Invalid("An unhit Crystal event cannot add progression.");
                }
            }
            if (state.fingerprint != Fingerprint(state.entries)) throw Invalid("Crystal owner fingerprint mismatch.");
        }

        private static void RequireProgressionTransition(CombatProgressionState progression, int fromLevel, int fromXp,
            int toLevel, int toXp, bool requireSettled)
        {
            if (progression == null || fromLevel < 1 || toLevel < fromLevel || fromXp < 0 || toXp < 0 ||
                (long)toLevel - fromLevel > fromXp)
                throw Invalid("Crystal reward progression transition is impossible.");
            int remaining = fromXp;
            // Levels consume the actual cloned Run rule table. The XP bound rejects impossible large spans before iteration.
            for (int level = fromLevel; level < toLevel; level++)
            {
                int required = progression.GetRequiredExperience(level);
                if (required <= 0 || remaining < required) throw Invalid("Crystal reward advanced without its real XP threshold.");
                remaining -= required;
            }
            if (remaining != toXp || (requireSettled && remaining >= progression.GetRequiredExperience(toLevel)))
                throw Invalid("Crystal rewards added/lost XP or left an unproven level pending.");
        }

        private static bool ValidRawExperience(SideEventCrystalSpecification source, DungeonRunCrystalEntry entry, int raw)
        {
            long scale = source.Reward.MultiplyByCompletedFloorPlusOneCappedToFloorCount
                ? Math.Min((long)entry.completedSourceFloors + 1, entry.sourceFloorCount) : 1;
            return raw >= source.Reward.MinimumHitXpInclusive * scale &&
                raw <= (source.Reward.MaximumHitXpExclusive - 1L) * scale && raw % scale == 0;
        }

        private static SideEventCrystalSpecification CanonicalSpecification() => CrystalEventContracts.GetSpecification(
            new LevelInteractionEffect { effectId = "effect.internal.crystal-specification", kind = LevelInteractionEffectKind.CrystalThrowing,
                crystalThrowing = new LevelInteractionCrystalContract { version = 1, sourceEventId = SourceId } });

        private static CrystalThrowingSession RestoreCore(DungeonRunCrystalEntry entry, LevelInteractionEffect effect)
            => RestoreCore(entry, CrystalEventContracts.GetSpecification(effect));
        private static CrystalThrowingSession RestoreCore(DungeonRunCrystalEntry entry, SideEventCrystalSpecification source)
        {
            if (entry == null || entry.hits == null || entry.rosterIds == null) throw Invalid("Crystal snapshot collections are missing.");
            try
            {
                return CrystalThrowingSession.Restore(new CrystalThrowingSnapshot(source, (CrystalThrowingStage)entry.stage,
                    Float(entry.eventElapsedBits), Float(entry.autoThrowElapsedBits), entry.heldButtonCount, entry.thrownCount,
                    entry.rosterIds, entry.hits.Select(h => h == null ? 0 : h.cardRuntimeInstanceId).ToArray()));
            }
            catch (ArgumentException ex) { throw Invalid("Crystal physical snapshot is invalid: " + ex.Message); }
        }

        private static void StoreCore(DungeonRunCrystalEntry entry, CrystalThrowingSnapshot snapshot)
        {
            entry.stage = (int)snapshot.Stage; entry.eventElapsedBits = Bits(snapshot.EventElapsed);
            entry.autoThrowElapsedBits = Bits(snapshot.AutoThrowElapsed); entry.heldButtonCount = snapshot.HeldButtonCount;
            entry.thrownCount = snapshot.ThrownCount; entry.rosterIds = snapshot.DrawPileTopFirst.ToList();
        }

        private DungeonRunCrystalEntry Find(string nodeId) => Find(_state, nodeId);
        private DungeonRunCrystalEntry Find(DungeonRunCrystalSaveData state, string nodeId)
            => state.entries.SingleOrDefault(e => e.floorContentId == _run.CurrentFloorContentId && e.nodeContentId == nodeId);
        private static DungeonRunCrystalSaveData Empty()
            => new DungeonRunCrystalSaveData { version = Version, fingerprint = Fingerprint(Array.Empty<DungeonRunCrystalEntry>()) };
        private static DungeonRunCrystalSaveData Clone(DungeonRunCrystalSaveData state)
        {
            if (state == null || state.entries == null) throw Invalid("Crystal save is missing.");
            return new DungeonRunCrystalSaveData { version = state.version, fingerprint = state.fingerprint,
                entries = state.entries.Select(CloneEntry).ToList() };
        }
        private static DungeonRunCrystalEntry CloneEntry(DungeonRunCrystalEntry e)
        {
            if (e == null || e.rosterIds == null || e.hits == null) throw Invalid("Crystal entry collections are missing.");
            return new DungeonRunCrystalEntry
            {
                floorContentId = e.floorContentId, nodeContentId = e.nodeContentId, effectId = e.effectId, sourceEventId = e.sourceEventId,
                completedSourceFloors = e.completedSourceFloors, sourceFloorCount = e.sourceFloorCount, stage = e.stage,
                initialLevel = e.initialLevel, initialExperience = e.initialExperience, initialCarryUnits = e.initialCarryUnits,
                finalLevel = e.finalLevel, finalExperience = e.finalExperience, finalCarryUnits = e.finalCarryUnits,
                eventElapsedBits = e.eventElapsedBits, autoThrowElapsedBits = e.autoThrowElapsedBits, exitElapsedBits = e.exitElapsedBits,
                heldButtonCount = e.heldButtonCount, thrownCount = e.thrownCount, finished = e.finished,
                rosterIds = new List<int>(e.rosterIds), hits = e.hits.Select(h => h == null ? null : new DungeonRunCrystalHitData
                { cardRuntimeInstanceId = h.cardRuntimeInstanceId, ordinal = h.ordinal, rawExperience = h.rawExperience,
                    actualExperience = h.actualExperience, growthPercent = h.growthPercent, levelBefore = h.levelBefore,
                    levelAfter = h.levelAfter, experienceBefore = h.experienceBefore, experienceAfter = h.experienceAfter,
                    carryUnits = h.carryUnits, requiredExperienceBefore = h.requiredExperienceBefore }).ToList()
            };
        }
        private static long SumExperience(IEnumerable<DungeonRunCrystalEntry> entries)
        { long sum = 0; foreach (var e in entries) foreach (var h in e.hits) sum = checked(sum + h.actualExperience); return sum; }
        private static string EntryFingerprint(DungeonRunCrystalEntry entry) => Fingerprint(new[] { entry });
        private static string Fingerprint(IEnumerable<DungeonRunCrystalEntry> entries)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write("crystal-run-lifecycle.v1"); var list = entries.ToArray(); writer.Write(list.Length);
                foreach (var e in list)
                {
                    if (e == null || e.rosterIds == null || e.hits == null) throw Invalid("Crystal fingerprint input is missing.");
                    WriteId(writer, e.floorContentId); WriteId(writer, e.nodeContentId); WriteId(writer, e.effectId); WriteId(writer, e.sourceEventId);
                    writer.Write(e.completedSourceFloors); writer.Write(e.sourceFloorCount); writer.Write(e.stage);
                    writer.Write(e.initialLevel); writer.Write(e.initialExperience); writer.Write(e.initialCarryUnits);
                    writer.Write(e.finalLevel); writer.Write(e.finalExperience); writer.Write(e.finalCarryUnits);
                    writer.Write(e.eventElapsedBits); writer.Write(e.autoThrowElapsedBits); writer.Write(e.exitElapsedBits);
                    writer.Write(e.heldButtonCount); writer.Write(e.thrownCount); writer.Write(e.finished);
                    writer.Write(e.rosterIds.Count); foreach (int id in e.rosterIds) writer.Write(id);
                    writer.Write(e.hits.Count); foreach (var h in e.hits)
                    {
                        if (h == null) throw Invalid("Crystal hit fingerprint input is missing.");
                        writer.Write(h.cardRuntimeInstanceId); writer.Write(h.ordinal); writer.Write(h.rawExperience);
                        writer.Write(h.actualExperience); writer.Write(h.growthPercent); writer.Write(h.levelBefore); writer.Write(h.levelAfter);
                        writer.Write(h.experienceBefore); writer.Write(h.experienceAfter); writer.Write(h.carryUnits); writer.Write(h.requiredExperienceBefore);
                    }
                }
                writer.Flush(); using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
        private static int HitSeed(string runId, string effectId, int physicalId)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, StrictUtf8, true))
            {
                writer.Write("crystal-hit-seed.v1"); WriteId(writer, runId); WriteId(writer, effectId); writer.Write(physicalId);
                writer.Flush(); using (var sha = SHA256.Create()) return BitConverter.ToInt32(sha.ComputeHash(stream.ToArray()), 0) & int.MaxValue;
            }
        }
        private static void WriteId(BinaryWriter writer, string value)
        { if (!Stable(value)) throw Invalid("Crystal stable identity is missing or invalid Unicode."); writer.Write(value); }
        private static bool Stable(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) return false;
            try { StrictUtf8.GetByteCount(value); return true; } catch (EncoderFallbackException) { return false; }
        }
        private static void RequireContext(int completed, int count)
        { if (count <= 0 || completed < 0 || completed > count) throw Invalid("Crystal source-floor context is invalid."); }
        private static bool EqualOptional(string a, string b) => (a ?? string.Empty) == (b ?? string.Empty);
        private static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        private static float Float(int bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static InvalidOperationException Invalid(string message) => new InvalidOperationException("Run Crystal: " + message);
    }

    [Serializable]
    internal sealed class DungeonRunCrystalSaveData
    {
        public int version;
        public string fingerprint;
        public List<DungeonRunCrystalEntry> entries = new List<DungeonRunCrystalEntry>();
    }
    [Serializable]
    internal sealed class DungeonRunCrystalEntry
    {
        public string floorContentId;
        public string nodeContentId;
        public string effectId;
        public string sourceEventId;
        public int completedSourceFloors;
        public int sourceFloorCount;
        public int initialLevel;
        public int initialExperience;
        public int initialCarryUnits;
        public int finalLevel;
        public int finalExperience;
        public int finalCarryUnits;
        public int stage;
        public int eventElapsedBits;
        public int autoThrowElapsedBits;
        public int exitElapsedBits;
        public int heldButtonCount;
        public int thrownCount;
        public bool finished;
        public List<int> rosterIds = new List<int>();
        public List<DungeonRunCrystalHitData> hits = new List<DungeonRunCrystalHitData>();
    }
    [Serializable]
    internal sealed class DungeonRunCrystalHitData
    {
        public int cardRuntimeInstanceId;
        public int ordinal;
        public int rawExperience;
        public int actualExperience;
        public int growthPercent;
        public int levelBefore;
        public int levelAfter;
        public int experienceBefore;
        public int experienceAfter;
        public int carryUnits;
        public int requiredExperienceBefore;
    }
}
