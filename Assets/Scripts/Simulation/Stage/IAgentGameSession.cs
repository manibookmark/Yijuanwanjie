using System;
using System.Collections.Generic;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>One externally revisioned, isolated playtest lifetime. A campaign
    /// owns successive Runs; the service owns neither Run nor account state.</summary>
    internal interface IAgentGameSession : IDisposable
    {
        int Revision { get; }
        bool IsTerminal { get; }
        string ContentFingerprint { get; }
        AgentGameObservation Observe();
        List<AgentGameAction> LegalActions();
        AgentGameTraceDocument ExportTrace();
        string CaptureRecoveryDigest();
        bool TryAct(string actionId, string summary, out string code, out string message);
        void Terminate(string outcome, string reason);
    }
}
