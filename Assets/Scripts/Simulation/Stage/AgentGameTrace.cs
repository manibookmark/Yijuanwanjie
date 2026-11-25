using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Stage
{
    [Serializable] public sealed class AgentGameTraceDocument
    {
        public string protocolVersion = AgentGameProtocol.Version;
        public string testCaseId, actorId, agentVersion, promptVersion, contentFingerprint, startingAccount;
        public int seed;
        public string outcome, terminationReason;
        public string replayCoverage = "Ordered external actions, typed events and public checkpoint hashes; full public observations are sparse checkpoints. No hidden state export. Production full-run replay not yet certified.";
        public AgentGameObservation initialObservation;
        public List<AgentGameTraceStep> steps = new List<AgentGameTraceStep>();
    }
    [Serializable] public sealed class AgentGameTraceStep
    {
        public int revision;
        public AgentGameAction action;
        public string decisionSummary, beforeHash, afterHash, status, error;
        public AgentGameObservation after;
        public List<AgentGameEvent> events = new List<AgentGameEvent>();
    }
    [Serializable] public sealed class AgentGameEvent
    {
        public string kind, contentId;
        public int runtimeInstanceId, amount, armor, healing, mana, jade, combo;
        public bool chainBroken, critical;
    }

    /// <summary>Owns detached replay evidence and the lifecycle of typed result
    /// subscriptions. Initial state and action evidence have priority over optional
    /// rolling public checkpoints; reclaiming a checkpoint never removes an action,
    /// its hashes or its typed events. Replaying creates another formal session and
    /// re-submits commands; it never trusts recorded results as gameplay state.</summary>
    internal sealed class AgentGameTrace : IDisposable
    {
        private readonly AgentGameTraceDocument _document;
        private readonly List<AgentGameEvent> _pending = new List<AgentGameEvent>();
        private const int MaximumCharacters = 8 * 1024 * 1024;
        private const int MinimumActionReserveCharacters = 256 * 1024;
        private const int PeriodicCheckpointInterval = 64;
        private readonly int _maximumCharacters, _minimumActionReserveCharacters;
        private readonly Queue<Checkpoint> _checkpoints = new Queue<Checkpoint>();
        private int _characters, _requiredCharacters, _metadataCharacters;
        private bool _eventsOmitted;
        internal bool HasActionCapacity => !_eventsOmitted &&
            _requiredCharacters < _maximumCharacters - _minimumActionReserveCharacters;
        private CombatController _combat;
        internal int StepCount => _document.steps.Count;
        internal List<AgentGameEvent> EventsAfter(int firstStep)
        {
            var events = new List<AgentGameEvent>();
            for (int index = firstStep; index < _document.steps.Count; index++)
                events.AddRange(_document.steps[index].events.ConvertAll(Clone));
            return events;
        }
        internal void AppendEvents(IEnumerable<AgentGameEvent> events)
        { if (events != null) foreach (AgentGameEvent value in events) _pending.Add(Clone(value)); }
        internal AgentGameTrace(AgentGameTraceDocument document,
            int maximumCharacters = MaximumCharacters,
            int minimumActionReserveCharacters = MinimumActionReserveCharacters)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (minimumActionReserveCharacters <= 0 || maximumCharacters <= minimumActionReserveCharacters)
                throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
            _document = document;
            _maximumCharacters = maximumCharacters;
            _minimumActionReserveCharacters = minimumActionReserveCharacters;
        }
        internal void SetInitial(AgentGameObservation observation)
        {
            _document.initialObservation = Clone(observation);
            _characters = _requiredCharacters = AgentGameJson.Trace(_document).Length;
            _metadataCharacters = MetadataLength();
        }
        internal void Attach(CombatController combat)
        {
            Detach(); _combat = combat;
            combat.OnCardPlayResolved += Card;
            combat.OnEnemyActionResolved += EnemyAction;
            combat.OnExperienceAwarded += Experience;
            combat.OnCombatEnded += Ended;
        }
        internal void Detach()
        {
            if (_combat == null) return;
            _combat.OnCardPlayResolved -= Card; _combat.OnEnemyActionResolved -= EnemyAction;
            _combat.OnExperienceAwarded -= Experience; _combat.OnCombatEnded -= Ended; _combat = null;
        }
        private void Card(CombatCardResolvedEvent e) => _pending.Add(new AgentGameEvent {
            kind = "CardResolved", contentId = e.Card?.contentId, runtimeInstanceId = e.Card?.runtimeInstanceId ?? 0,
            amount = e.DamageDealt, armor = e.ArmorGained, healing = e.HealAmount, mana = e.ManaGained,
            jade = e.JadeGained, combo = e.ComboCount, chainBroken = e.ChainWasBroken, critical = e.IsCritical });
        private void EnemyAction(CombatEnemyActionResult e) => _pending.Add(new AgentGameEvent {
            kind = "EnemyActionResult", amount = e.PlayerHealthDamage, armor = e.PlayerArmorDamage });
        private void Experience(CombatExperienceAward e) => _pending.Add(new AgentGameEvent { kind = "ExperienceAwarded", amount = e.ActualExperience });
        private void Ended(CombatEndedEvent e) => _pending.Add(new AgentGameEvent { kind = "Combat" + e.Outcome });
        internal bool Add(int revision, AgentGameAction action, string summary, AgentGameObservation before,
            AgentGameObservation after, string status = "Committed", string error = null)
        {
            var step = new AgentGameTraceStep { revision = revision, action = Clone(action), decisionSummary = summary,
                beforeHash = Hash(JsonUtility.ToJson(before)), afterHash = Hash(JsonUtility.ToJson(after)),
                status = status, error = error, events = new List<AgentGameEvent>(_pending) };

            _document.outcome = after.outcome;
            _document.terminationReason = after.terminationReason;
            UpdateMetadataLength();
            int separatorLength = _document.steps.Count > 0 ? 1 : 0;
            int length = AgentGameJson.TraceStep(step).Length + separatorLength;
            // Reclamation can also change the coverage metadata. Include that
            // envelope change before deciding whether mandatory evidence fits.
            ReclaimCheckpoints(length);
            bool fits = _requiredCharacters + length <= _maximumCharacters;
            if (!fits)
            {
                // An exceptional single action can exceed the mandatory reserve.
                // Keep its command and hashes for truthful termination evidence,
                // explicitly mark missing events and never resume this trace.
                step.events.Clear();
                _eventsOmitted = true;
                _document.replayCoverage = "Ordered external actions and public checkpoint hashes; some typed events and full public observations were omitted near the bounded trace limit. No hidden state export. Production full-run replay not yet certified.";
                UpdateMetadataLength();
                length = AgentGameJson.TraceStep(step).Length + separatorLength;
                ReclaimCheckpoints(length);
            }

            _document.steps.Add(step);
            _characters += length;
            _requiredCharacters += length;
            if (fits && ShouldCheckpoint(revision, before, after)) RetainCheckpoint(step, after);
            _pending.Clear();
            return fits;
        }

        private void RetainCheckpoint(AgentGameTraceStep step, AgentGameObservation observation)
        {
            int requiredLength = AgentGameJson.TraceStep(step).Length;
            AgentGameObservation checkpointObservation = Clone(observation);
            step.after = checkpointObservation;
            int checkpointLength = AgentGameJson.TraceStep(step).Length - requiredLength;
            step.after = null;
            if (_requiredCharacters + checkpointLength >
                _maximumCharacters - _minimumActionReserveCharacters)
            {
                MarkOptionalCheckpointOmitted();
                return;
            }
            ReclaimCheckpoints(checkpointLength);
            if (_characters + checkpointLength >
                _maximumCharacters - _minimumActionReserveCharacters)
            {
                MarkOptionalCheckpointOmitted();
                return;
            }
            step.after = checkpointObservation;
            _checkpoints.Enqueue(new Checkpoint(step, checkpointLength));
            _characters += checkpointLength;
        }

        private void ReclaimCheckpoints(int incomingCharacters)
        {
            while (_checkpoints.Count > 0 && _characters + incomingCharacters >
                _maximumCharacters - _minimumActionReserveCharacters)
            {
                Checkpoint checkpoint = _checkpoints.Dequeue();
                checkpoint.Step.after = null;
                _characters -= checkpoint.Characters;
                MarkOptionalCheckpointOmitted();
            }
        }

        private void MarkOptionalCheckpointOmitted()
        {
            if (_eventsOmitted) return;
            _document.replayCoverage = "Ordered external actions, typed events and public checkpoint hashes; full public observations are optional rolling checkpoints and older checkpoints may be evicted near the bounded trace limit. No hidden state export. Production full-run replay not yet certified.";
            UpdateMetadataLength();
        }

        // Metadata changes alter the serialized envelope too. Counting their
        // escaped JSON length keeps admission independent of optional snapshots.
        private int MetadataLength() => JsonUtility.ToJson(new Metadata {
            outcome = _document.outcome, terminationReason = _document.terminationReason,
            replayCoverage = _document.replayCoverage }).Length;

        private void UpdateMetadataLength()
        {
            int length = MetadataLength();
            int change = length - _metadataCharacters;
            _characters += change;
            _requiredCharacters += change;
            _metadataCharacters = length;
        }

        [Serializable] private sealed class Metadata
        { public string outcome, terminationReason, replayCoverage; }

        private readonly struct Checkpoint
        {
            internal readonly AgentGameTraceStep Step;
            internal readonly int Characters;
            internal Checkpoint(AgentGameTraceStep step, int characters)
            { Step = step; Characters = characters; }
        }

        private static bool ShouldCheckpoint(int revision, AgentGameObservation before, AgentGameObservation after)
        {
            if (revision == 1 || revision % PeriodicCheckpointInterval == 0 || before == null || after == null) return true;
            return before.phase != after.phase || before.floorContentId != after.floorContentId || before.outcome != after.outcome;
        }
        internal AgentGameTraceDocument Export()
        {
            AgentGameTraceDocument copy = Clone(_document);
            // JsonUtility's inline-class roundtrip invents an empty observation
            // for null checkpoints. Absence belongs to this owner, not to DTO
            // defaults; preserve it before handing out the detached document.
            for (int i = 0; i < _document.steps.Count; i++)
                if (_document.steps[i].after == null) copy.steps[i].after = null;
            return copy;
        }
        internal static T Clone<T>(T value) => value == null ? default : JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        internal static string Hash(string text)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty))).Replace("-", "").ToLowerInvariant();
        }
        internal static bool VerifyReplay(AgentGameTraceDocument document, Func<AgentGameSession> createSession, out string error)
        {
            error = null;
            using (AgentGameSession replay = createSession())
            {
                if (replay.ContentFingerprint != document.contentFingerprint)
                { error = "Content fingerprint differs; replay is not comparable."; return false; }
                if (Hash(JsonUtility.ToJson(replay.Observe())) != Hash(JsonUtility.ToJson(document.initialObservation)))
                { error = "Initial public checkpoint differs."; return false; }
                foreach (AgentGameTraceStep step in document.steps)
                {
                    if (step.status != "Committed") { error = "Trace includes an interrupted/terminal administrative step; only its preceding committed prefix is replayable."; return false; }
                    if (!replay.TryAct(step.action.actionId, step.decisionSummary, out string code, out string message))
                    { error = code + ": " + message; return false; }
                    if (Hash(JsonUtility.ToJson(replay.Observe())) != step.afterHash)
                    { error = "Public checkpoint differs at revision " + step.revision; return false; }
                }
                return true;
            }
        }
        public void Dispose() { Detach(); _pending.Clear(); _checkpoints.Clear(); }
    }
}
