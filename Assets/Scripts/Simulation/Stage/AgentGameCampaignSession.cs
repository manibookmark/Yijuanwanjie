using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    internal enum AgentGameCampaignStart { FreshTutorial, G3ClearedHub, G3HistoricalHub }

    [Serializable] public sealed class AgentGameCampaignState
    {
        public int runOrdinal, runSeed;
        public string runEntryContentId, runId, leaderCrawlerContentId, runOutcome;
        public List<AgentGameCampaignRunResult> completedRuns = new List<AgentGameCampaignRunResult>();
    }
    [Serializable] public sealed class AgentGameCampaignRunResult
    {
        public int runOrdinal, runSeed;
        public string runEntryContentId, runId, leaderCrawlerContentId, outcome;
        public AgentGameSettlement settlement;
    }

    /// <summary>Owns the isolated account's successive Run lifetimes and Hub
    /// boundary. Each child owns one ordinary Run; the account adapter delegates
    /// permanent changes to Meta. No rule, reward, route or player choice is
    /// reproduced here. Global revisions and evidence survive child disposal.</summary>
    internal sealed class AgentGameCampaignSession : IAgentGameSession
    {
        internal const int MaximumActions = 16384;
        private readonly int _seed;
        private readonly string _testCaseId, _startingAccount;
        private readonly string _actorId, _agentVersion, _promptVersion;
        private readonly AgentGameCampaignAccount _account;
        private readonly Func<MetaProgression, string, string, AgentGameEntry> _createEntry;
        private readonly AgentGameTrace _trace;
        private readonly List<AgentGameCampaignRunResult> _completed = new List<AgentGameCampaignRunResult>();
        private AgentGameSession _child;
        private AgentGameObservation _observation;
        private List<AgentGameCommand> _commands;
        private int _runOrdinal, _runSeed;
        private string _entryId, _runId, _leaderId;
        private string _outcome = "InProgress", _reason;
        private bool _disposed;

        public int Revision { get; private set; }
        public bool IsTerminal => _outcome != "InProgress";
        public string ContentFingerprint { get; }

        internal AgentGameCampaignSession(int seed, string actorId = null,
            string agentVersion = null, string promptVersion = null,
            AgentGameCampaignAccount account = null,
            Func<MetaProgression, string, string, AgentGameEntry> createEntry = null,
            string contentFingerprint = null,
            AgentGameCampaignStart start = AgentGameCampaignStart.FreshTutorial)
        {
            if (start != AgentGameCampaignStart.FreshTutorial && start != AgentGameCampaignStart.G3ClearedHub &&
                start != AgentGameCampaignStart.G3HistoricalHub)
                throw new ArgumentOutOfRangeException(nameof(start));
            _seed = seed; _actorId = actorId; _agentVersion = agentVersion; _promptVersion = promptVersion;
            bool historical = start == AgentGameCampaignStart.G3HistoricalHub;
            bool startsAtHub = start != AgentGameCampaignStart.FreshTutorial;
            _testCaseId = historical ? AgentGameProtocol.G4HistoricalCampaignTestCaseId :
                startsAtHub ? AgentGameProtocol.G4CampaignTestCaseId : AgentGameProtocol.CampaignTestCaseId;
            _account = account ?? (historical ? AgentGameCampaignAccount.G3HistoricalForG4() :
                startsAtHub ? AgentGameCampaignAccount.G3ClearedForG4() : new AgentGameCampaignAccount());
            _startingAccount = historical
                ? _account.InitialRecipeDeclaration + "; starts at Hub; G4 clearance and Shawujing remain ungranted. " +
                  "Subsequent Runs retain this same account. Run seeds = campaign seed + run ordinal - 1; ordinal starts at 1 for the first legally selected dungeon."
                : startsAtHub
                ? "Declared G3Cleared permanent-unlock blueprint; starts at Hub with Zhubajie, zero currency/upgrades/equipped Arcana. G4 clearance is not granted; Shawujing remains locked. No prior G3 temporary deck or user save imported; not a naturally played G1-G3 account. Subsequent Runs retain this same account. Run seeds = campaign seed + run ordinal - 1; ordinal starts at 1 for the first legally selected dungeon."
                : "Fresh zero-progress account; real tutorial first. Same Meta account across formal Runs; no user save or progression blueprint imported. Run seeds = campaign seed + run ordinal - 1; ordinal starts at 1 for tutorial.";
            _createEntry = createEntry ?? ((progress, hero, entry) =>
                AgentGameEntry.Campaign(progress, hero, entry, ContentFingerprint));
            ContentFingerprint = contentFingerprint ?? AgentGameTrace.Hash(
                AgentGameEntry.CampaignContentFingerprint() + AgentGameCampaignAccount.FingerprintPayload +
                "|campaign-start.v2|" + _testCaseId + "|" + _startingAccount + "|" + _account.CaptureRecoveryDigest() +
                _account.InitialRecipeFingerprint);
            _trace = new AgentGameTrace(new AgentGameTraceDocument {
                testCaseId = _testCaseId, seed = seed, actorId = actorId,
                agentVersion = agentVersion, promptVersion = promptVersion,
                contentFingerprint = ContentFingerprint,
                startingAccount = _startingAccount },
                maximumCharacters: 32 * 1024 * 1024);
            try { if (!startsAtHub) StartRun(RunCombatRules.TutorialLevelContentId); Refresh(); _trace.SetInitial(_observation); }
            catch { Dispose(); throw; }
        }

        internal static IAgentGameSession Create(string testCaseId, int seed,
            string actor, string agent, string prompt)
        {
            if (!AgentGameProtocol.IsCampaignTestCase(testCaseId))
                throw new ArgumentException("Unknown campaign test case.", nameof(testCaseId));
            return new AgentGameCampaignSession(seed, actor, agent, prompt,
                start: testCaseId == AgentGameProtocol.G4HistoricalCampaignTestCaseId
                    ? AgentGameCampaignStart.G3HistoricalHub : testCaseId == AgentGameProtocol.G4CampaignTestCaseId
                    ? AgentGameCampaignStart.G3ClearedHub : AgentGameCampaignStart.FreshTutorial);
        }

        private void StartRun(string entryId)
        {
            if (_child != null) throw new InvalidOperationException("Return to Hub before starting another Run.");
            int ordinal = checked(_runOrdinal + 1);
            int seed = unchecked(_seed + ordinal - 1);
            string suffix = "campaign-run-" + ordinal.ToString(CultureInfo.InvariantCulture);
            AgentGameEntry entry = _createEntry(_account.Account, _account.SelectedHeroId, entryId);
            entry.TestCaseId = _testCaseId;
            entry.StartingAccount = _startingAccount + " Current formal entry=" + entryId +
                "; selected hero=" + _account.SelectedHeroId + "; no additional blueprint replay at Run start.";
            var next = new AgentGameSession(entry, seed, _actorId, _agentVersion, _promptVersion, suffix);
            _child = next; _runOrdinal = ordinal; _runSeed = seed; _entryId = entryId;
            _runId = "agent-game/" + entry.TestCaseId + "/" + seed.ToString(CultureInfo.InvariantCulture) + "/" + suffix;
            _leaderId = entry.LeaderCrawlerContentId;
        }

        private void ReturnToHub()
        {
            if (_child == null || _child.Observe().outcome != "Victory")
                throw new InvalidOperationException("Only a genuinely settled victory can return to the campaign Hub.");
            AgentGameObservation last = _child.Observe();
            _completed.Add(new AgentGameCampaignRunResult { runOrdinal = _runOrdinal, runSeed = _runSeed,
                runEntryContentId = _entryId, runId = _runId, leaderCrawlerContentId = _leaderId,
                outcome = last.outcome, settlement = last.settlement });
            _child.Dispose(); _child = null;
            if (_entryId == RunCombatRules.TutorialLevelContentId) _account.AcceptTutorialSelection();
        }

        public AgentGameObservation Observe() { ThrowIfDisposed(); return AgentGameTrace.Clone(_observation); }
        public List<AgentGameAction> LegalActions()
        { ThrowIfDisposed(); return _commands.Select(c => AgentGameTrace.Clone(c.View)).ToList(); }
        public AgentGameTraceDocument ExportTrace() { ThrowIfDisposed(); return _trace.Export(); }
        public string CaptureRecoveryDigest() => AgentGameTrace.Hash(
            _testCaseId + "|" + _account.CaptureRecoveryDigest() + "|" + _runOrdinal + "|" + _entryId + "|" + _runId + "|" +
            _runSeed + "|" + _outcome + "|" + _reason + "|" + Revision + "|" +
            JsonUtility.ToJson(new AgentGameCampaignState { completedRuns = _completed }) + "|" +
            (_child?.CaptureRecoveryDigest() ?? "Hub"));

        public bool TryAct(string actionId, string summary, out string code, out string message)
        {
            ThrowIfDisposed(); code = null; message = null;
            AgentGameCommand command = _commands.FirstOrDefault(c => c.View.actionId == actionId);
            if (IsTerminal || command == null) { code = "InvalidAction"; message = "Action is absent from the current legalActions."; return false; }
            if (Revision >= MaximumActions || !_trace.HasActionCapacity)
            { Terminate("BudgetExceeded", "Campaign action or evidence budget reached."); code = "BudgetExceeded"; message = _reason; return false; }
            AgentGameObservation before = _observation;
            try
            {
                // Child actions are dispatched directly so failures retain their
                // real outcome rather than being relabelled as a Hub exception.
                bool succeeded = true;
                string prefix = "r" + Revision + "/run/";
                if (_child != null && actionId.StartsWith(prefix, StringComparison.Ordinal))
                {
                    succeeded = _child.TryAct(actionId.Substring(prefix.Length), summary, out code, out message);
                    _trace.AppendEvents(_child.LastActionEvents);
                    AgentGameObservation child = _child.Observe();
                    if (child.outcome != "InProgress" && child.outcome != "Victory")
                    { _outcome = child.outcome; _reason = child.terminationReason; }
                }
                else command.Execute();
                Revision++; Refresh();
                if (!_trace.Add(Revision, command.View, summary, before, _observation,
                        succeeded ? "Committed" : code, succeeded ? null : message))
                { Terminate("BudgetExceeded", "Campaign evidence budget reached after commit."); code = "BudgetExceeded"; message = _reason; return false; }
                return succeeded;
            }
            catch (Exception exception)
            {
                Revision++; _outcome = "ExecutionError"; _reason = exception.GetType().Name + ": " + exception.Message;
                try { Refresh(); } catch { _observation = AgentGameTrace.Clone(before); _observation.outcome = _outcome;
                    _observation.phase = _outcome; _observation.terminationReason = _reason; _commands.Clear(); }
                _trace.Add(Revision, command.View, summary, before, _observation, "ExecutionError", _reason);
                code = "ExecutionError"; message = _reason; return false;
            }
        }

        private void Refresh()
        {
            var commands = new List<AgentGameCommand>();
            if (_child != null)
            {
                _observation = _child.Observe();
                if (!IsTerminal && _observation.outcome == "Victory")
                {
                    _observation.phase = "RunVictory";
                    commands.Add(new AgentGameCommand(new AgentGameAction { actionId = "hub/return",
                        kind = "ReturnToHub", title = "回仙域", description = "保留本关正式结算，返回同一账号的仙域。" }, ReturnToHub));
                }
                else if (!IsTerminal)
                    foreach (AgentGameAction childAction in _child.LegalActions())
                    {
                        AgentGameAction view = AgentGameTrace.Clone(childAction);
                        view.actionId = "run/" + view.actionId;
                        commands.Add(new AgentGameCommand(view, () => { }));
                    }
            }
            else
            {
                _observation = new AgentGameObservation { phase = "Hub", player = new AgentGamePlayer { jade = _account.Account.lingyu } };
                if (!IsTerminal)
                {
                    commands.AddRange(_account.Commands(profile => StartRun(profile.contentId)));
                    commands.Add(new AgentGameCommand(new AgentGameAction { actionId = "campaign/finish", kind = "FinishCampaign",
                        title = "结束本次模拟", description = "保留已发生结果；这不发放通关奖励，也不声明未完成内容已通过。" },
                        () => { _outcome = "Completed"; _reason = "Finished by external player at Hub."; }));
                }
            }
            _observation.testCaseId = _testCaseId;
            _observation.outcome = _outcome; _observation.terminationReason = _reason;
            if (IsTerminal) _observation.phase = _outcome;
            _observation.account = _account.Snapshot();
            _observation.campaign = new AgentGameCampaignState { runOrdinal = _runOrdinal, runSeed = _runSeed,
                runEntryContentId = _entryId, runId = _runId, leaderCrawlerContentId = _leaderId,
                runOutcome = _child?.Observe().outcome,
                completedRuns = _completed.Select(AgentGameTrace.Clone).ToList() };
            foreach (AgentGameCommand command in commands) command.View.actionId = "r" + Revision + "/" + command.View.actionId;
            _commands = IsTerminal ? new List<AgentGameCommand>() : commands;
        }

        public void Terminate(string outcome, string reason)
        {
            ThrowIfDisposed(); if (IsTerminal) return;
            AgentGameObservation before = _observation;
            _outcome = outcome; _reason = reason; Revision++;
            Refresh();
            _trace.Add(Revision, new AgentGameAction { actionId = "administrative/" + outcome, kind = outcome },
                null, before, _observation, outcome);
        }
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(AgentGameCampaignSession)); }
        public void Dispose()
        { if (_disposed) return; _disposed = true; _child?.Dispose(); _child = null; _trace?.Dispose(); }
    }
}
