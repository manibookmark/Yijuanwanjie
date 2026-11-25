using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Main-thread JSON service for isolated playtests. Memory construction
    /// is inert; durable construction verifies and replays committed mutations.
    /// This owner alone registers sessions. Durable receipts belong only to the
    /// recovery archive; memory-only services use the bounded receipt journal.
    /// Recovery executes every committed request before comparing its receipt
    /// and state digest. It never treats an archived receipt as reconstructed
    /// session state. Recovery adds no gameplay or user-save rules.</summary>
    public sealed class AgentGameService : IDisposable
    {
        private const int MaximumLiveSessions = 4;
        private readonly int _threadId;
        private readonly Func<string, AgentGameEntry> _createEntry;
        private readonly Func<string, int, string, string, string, IAgentGameSession> _createCampaign;
        private readonly Dictionary<string, IAgentGameSession> _sessions = new Dictionary<string, IAgentGameSession>(StringComparer.Ordinal);
        private readonly HashSet<string> _closed = new HashSet<string>(StringComparer.Ordinal);
        private readonly AgentGameReceiptJournal _journal;
        private readonly AgentGameRecoveryJournal _recovery;
        private readonly string _hostKind, _engineVersion;
        private bool _persistenceFailed;
        private bool _disposed, _handling;
        private string[] _recoveredSessionIds = Array.Empty<string>();

        public int RecoveredReceiptCount { get; private set; }
        public long DiscardedRecoveryTailBytes => _recovery?.DiscardedTailBytes ?? 0;
        public bool IsDurable => _recovery != null;
        public bool PersistenceEnabled => IsDurable;
        public int RecoveryRecordCount => _recovery?.RecordCount ?? 0;
        public string BuildId => _recovery?.BuildId ?? string.Empty;
        public string[] RecoveredSessionIds => (string[])_recoveredSessionIds.Clone();
        public bool RecoveryRequired => _persistenceFailed;

        public AgentGameService() : this(AgentGameEntry.Production) { }
        /// <summary>Reconstructs only committed requests using the same frozen build.
        /// The directory is an isolated playtest archive, never the user save.</summary>
        public AgentGameService(string persistenceDirectory, string buildId)
            : this(AgentGameEntry.Production, null,
                new AgentGameRecoveryJournal(persistenceDirectory, buildId)) { }

        internal AgentGameService(Func<string, AgentGameEntry> createEntry,
            AgentGameReceiptJournal journal = null, AgentGameRecoveryJournal recovery = null,
            Func<string, int, string, string, string, IAgentGameSession> createCampaign = null)
        {
            if (journal != null && recovery != null)
                throw new ArgumentException("Memory and durable receipt owners are mutually exclusive.", nameof(journal));
            _threadId = Thread.CurrentThread.ManagedThreadId;
            _createEntry = createEntry ?? throw new ArgumentNullException(nameof(createEntry));
            _createCampaign = createCampaign ?? AgentGameCampaignSession.Create;
            _journal = recovery == null ? journal ??
                new AgentGameReceiptJournal(MaximumLiveSessions, AgentGameCampaignSession.MaximumActions) : null;
            _recovery = recovery;
            // Capture Unity host facts on the owning main thread once. Even a
            // wrong-thread error must not query Unity APIs or invent a package ID.
            _hostKind = AgentGameHostEnvironment.HostKind(PersistenceEnabled);
            _engineVersion = Application.unityVersion;
            try
            {
                if (_recovery != null)
                    foreach (AgentGameRecoveryJournal.Record record in _recovery.Records)
                    {
                        AgentGameRequest request = JsonUtility.FromJson<AgentGameRequest>(record.request);
                        if (request == null || JsonUtility.ToJson(request) != record.request ||
                            !IsMutation(request.operation))
                            throw new InvalidDataException("Recovery contains a noncanonical mutation request.");
                        AgentGameResponse receipt = JsonUtility.FromJson<AgentGameResponse>(record.response);
                        string recovered = HandleMutation(request, record, receipt?.sessionId);
                        if (recovered != record.response)
                            throw new InvalidDataException("Recovery request was rejected before replay.");
                        RecoveredReceiptCount++;
                    }
                _recoveredSessionIds = _sessions.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            }
            catch { Dispose(); throw; }
        }

        public string HandleJson(string requestJson)
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId)
                return Serialize(Error("InvalidRequest", "Service may only be called on its owning main thread."));
            if (_disposed) return Serialize(Error("SessionExpired", "Service is disposed; sessions were not reopened."));
            if (_handling) return Serialize(Error("InvalidRequest", "A service mutation is already executing."));
            AgentGameRequest request;
            try { request = AgentGameProtocol.Parse(requestJson); }
            catch (Exception exception) { return Serialize(Error("InvalidRequest", exception.Message)); }
            if (_persistenceFailed && request.operation != "describe" && request.operation != "receipt")
                return Serialize(Error("RecoveryRequired", "A persistence failure stopped this service. Reopen the same frozen archive and observe its committed revision before deciding again.", request.sessionId));
            bool mutation = IsMutation(request.operation);
            _handling = true;
            try
            {
                if (!mutation) return Execute(request, false);
                return HandleMutation(request);
            }
            finally { _handling = false; }
        }

        private static bool IsMutation(string operation)
            => operation == "create" || operation == "act" || operation == "close";

        private string HandleMutation(AgentGameRequest request,
            AgentGameRecoveryJournal.Record replay = null, string restoredSessionId = null)
        {
            bool releasesLiveSession = request.operation == "close" &&
                _sessions.TryGetValue(request.sessionId, out IAgentGameSession closing) &&
                request.expectedRevision == closing.Revision;
            try
            {
                string canonical = JsonUtility.ToJson(request);
                if (_recovery != null)
                {
                    // Recovery must execute committed commands, not return the
                    // receipts that already exist in the archive being replayed.
                    if (replay == null)
                    {
                        AgentGameRecoveryJournal.Record prior = _recovery.FindRecord(request.requestId);
                        if (prior != null)
                            return prior.request == canonical ? prior.response :
                                Serialize(Error("RequestConflict", "requestId was already used with different request content.", request.sessionId));
                    }
                    // Normalization can expand a legal wire request. Reject it
                    // before Execute can mutate the isolated session.
                    if (canonical.Length > AgentGameProtocol.MaximumRequestCharacters)
                        return Serialize(Error("InvalidRequest", "Normalized request exceeds the safe receipt size.", request.sessionId));
                }
                Func<string> execute = () =>
                {
                    if (replay == null) _recovery?.RequireAdmission(releasesLiveSession);
                    AgentGameDescription historicalDescription = replay == null ? null :
                        JsonUtility.FromJson<AgentGameResponse>(replay.response)?.description;
                    string response = Execute(request, true, restoredSessionId, historicalDescription);
                    if (_recovery != null)
                    {
                        string digest = CaptureRecoveryDigest();
                        if (replay != null)
                        {
                            if (response != replay.response || digest != replay.stateDigest)
                                throw new InvalidDataException("Recovery diverged at request " + request.requestId +
                                    "; responseMatch=" + (response == replay.response) + "; stateMatch=" + (digest == replay.stateDigest) + ".");
                        }
                        else _recovery.Append(canonical, response, digest);
                    }
                    return response;
                };
                if (_recovery != null) return execute();
                AgentGameReceiptJournal.Result result = _journal.Execute(request, releasesLiveSession, execute);
                if (result.Status == AgentGameReceiptJournal.Outcome.RequestConflict)
                    return Serialize(Error("RequestConflict", "requestId was already used with different request content.", request.sessionId));
                if (result.Status == AgentGameReceiptJournal.Outcome.RequestTooLarge)
                    return Serialize(Error("InvalidRequest", "Normalized request exceeds the safe receipt size.", request.sessionId));
                if (result.Status == AgentGameReceiptJournal.Outcome.BudgetExceeded)
                    return Serialize(Error("BudgetExceeded", "Request journal is at its safety limit. Observe, trace, close live sessions, and replay existing request IDs remain available.", request.sessionId));
                return result.Response;
            }
            catch (AgentGameRecoveryJournal.AdmissionException)
            {
                return Serialize(Error("BudgetExceeded", "The recovery archive has reached its safety limit. Existing receipts and closing live sessions remain available.", request.sessionId));
            }
            catch when (replay == null && _recovery != null)
            {
                // The command may already have changed isolated memory. Neither
                // a success receipt nor a rollback is claimed after failed IO.
                _persistenceFailed = true;
                return Serialize(Error("PersistenceFailure", "The command was not acknowledged durably. Further writes stopped; reopen the frozen archive and observe before retrying the same request ID.", request.sessionId));
            }
        }

        private string CaptureRecoveryDigest()
        {
            var value = new System.Text.StringBuilder();
            foreach (var pair in _sessions.OrderBy(p => p.Key, StringComparer.Ordinal))
                value.Append(pair.Key).Append(':').Append(pair.Value.CaptureRecoveryDigest()).Append('\n');
            foreach (string id in _closed.OrderBy(id => id, StringComparer.Ordinal))
                value.Append("closed:").Append(id).Append('\n');
            return AgentGameTrace.Hash(value.ToString());
        }

        private string Execute(AgentGameRequest request, bool mutation, string restoredSessionId = null,
            AgentGameDescription historicalDescription = null)
        {
            if (request.operation == "receipt") return ReadCommittedReceipt(request);
            AgentGameResponse response;
            try { response = Dispatch(request, restoredSessionId); }
            catch (Exception exception)
            {
                if (request.sessionId != null && _sessions.TryGetValue(request.sessionId, out IAgentGameSession failed))
                {
                    try { failed.Terminate("ExecutionError", exception.GetType().Name + ": " + exception.Message); }
                    catch { failed.Dispose(); _sessions.Remove(request.sessionId); _closed.Add(request.sessionId); }
                }
                response = Error("ExecutionError", exception.GetType().Name + ": " + exception.Message, request.sessionId);
            }
            string json = Serialize(response, historicalDescription);
            // Traces can be larger than ordinary state responses. Their
            // retrieval is read-only and is not retained in the journal.
            if (mutation && json.Length > AgentGameReceiptJournal.MaximumResponseCharacters)
            {
                string id = response.sessionId ?? request.sessionId;
                if (id != null && _sessions.TryGetValue(id, out IAgentGameSession oversized))
                    oversized.Terminate("BudgetExceeded", "Mutation response exceeded the safe journal size.");
                json = Serialize(Error("BudgetExceeded", "Response exceeded journal budget; session terminated, not rolled back.", id), historicalDescription);
            }
            return json;
        }

        private string ReadCommittedReceipt(AgentGameRequest query)
        {
            if (_recovery == null)
                return Serialize(Error("RecoveryUnavailable", "Committed receipt lookup requires a durable simulation archive."));
            AgentGameRequest original;
            try { original = AgentGameProtocol.Parse(query.originalRequestJson); }
            catch (Exception exception) { return Serialize(Error("InvalidRequest", exception.Message)); }
            if (!IsMutation(original.operation))
                return Serialize(Error("InvalidRequest", "Receipt lookup accepts only an original create, act or close request."));
            AgentGameRecoveryJournal.Record record = _recovery.FindRecord(original.requestId);
            if (record == null)
                return Serialize(Error("ReceiptNotFound", "The original request has no committed receipt; it was not executed by this lookup.", original.sessionId));
            if (record.request != JsonUtility.ToJson(original))
                return Serialize(Error("RequestConflict", "requestId was committed with different request content.", original.sessionId));
            return record.response;
        }

        private AgentGameResponse Dispatch(AgentGameRequest request, string restoredSessionId)
        {
            if (request.operation == "describe")
                return new AgentGameResponse { success = true, phase = "Description" };
            if (request.operation == "create")
            {
                if (!AgentGameProtocol.IsSupportedTestCase(request.testCaseId))
                    return Error("InvalidRequest", "Unknown testCaseId. Use describe for enabled entries.");
                if (_sessions.Count >= MaximumLiveSessions) return Error("BudgetExceeded", "Maximum live sessions reached; close a session first.");
                IAgentGameSession session = AgentGameProtocol.IsCampaignTestCase(request.testCaseId)
                    ? _createCampaign(request.testCaseId, request.seed, request.actorId, request.agentVersion, request.promptVersion)
                    : new AgentGameSession(_createEntry(request.testCaseId), request.seed,
                    request.actorId, request.agentVersion, request.promptVersion);
                string id = restoredSessionId ?? Guid.NewGuid().ToString("N");
                _sessions.Add(id, session);
                return State(id, session);
            }
            if (!_sessions.TryGetValue(request.sessionId, out IAgentGameSession current))
                return Error(_closed.Contains(request.sessionId) ? "SessionExpired" : "UnknownSession", "Session does not exist in this service lifecycle.", request.sessionId);
            if (request.operation == "observe") return State(request.sessionId, current);
            if (request.operation == "trace")
            { AgentGameResponse result = State(request.sessionId, current); result.trace = current.ExportTrace(); return result; }
            if (request.expectedRevision != current.Revision)
                return StateError(request.sessionId, current, "StaleRevision", "expectedRevision does not match the current revision; observe and decide again.");
            if (request.operation == "close")
            {
                try
                {
                    current.Terminate("Aborted", "Closed by caller.");
                    return State(request.sessionId, current);
                }
                finally
                {
                    // Release admission is valid only once per live session,
                    // including a failure while producing its closing snapshot.
                    try { current.Dispose(); }
                    finally { _sessions.Remove(request.sessionId); _closed.Add(request.sessionId); }
                }
            }
            return current.TryAct(request.actionId, request.decisionSummary, out string code, out string message)
                ? State(request.sessionId, current) : StateError(request.sessionId, current, code, message);
        }

        private static AgentGameResponse State(string id, IAgentGameSession session)
        {
            AgentGameObservation observation = session.Observe();
            return new AgentGameResponse { success = true, sessionId = id, revision = session.Revision,
                contentFingerprint = session.ContentFingerprint,
                phase = observation.phase, observation = observation, legalActions = session.LegalActions() };
        }
        private static AgentGameResponse StateError(string id, IAgentGameSession session, string code, string message)
        { AgentGameResponse response = State(id, session); response.success = false; response.error = new AgentGameError { code = code, message = message }; return response; }
        private static AgentGameResponse Error(string code, string message, string id = null)
            => new AgentGameResponse { success = false, sessionId = id, phase = "Error", error = new AgentGameError { code = code, message = message } };
        private string Serialize(AgentGameResponse response, AgentGameDescription historicalDescription = null)
        {
            // New reads and errors describe the current host/recovery state.
            // Replayed mutations retain the original metadata as part of their
            // frozen receipt; journal/receipt lookup returns those exact bytes.
            response.description = historicalDescription ?? new AgentGameDescription {
                hostKind = _hostKind, buildId = BuildId, engineVersion = _engineVersion,
                buildIdentityKind = IsDurable ? "frozen-archive" : "unfrozen-memory",
                persistenceEnabled = PersistenceEnabled, recoveredMutations = RecoveredReceiptCount,
                recoveredSessionIds = RecoveredSessionIds
            };
            return AgentGameJson.Response(response);
        }
        public void Dispose()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId) throw new InvalidOperationException("Dispose must run on the owning main thread.");
            if (_disposed) return;
            _disposed = true;
            try { foreach (IAgentGameSession session in _sessions.Values) session.Dispose(); }
            finally
            {
                _sessions.Clear(); _closed.Clear();
                try { _journal?.Dispose(); }
                finally { _recovery?.Dispose(); }
            }
        }
    }
}
