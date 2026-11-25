using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Host
{
    /// <summary>One main-thread owner for a dedicated process generation. It
    /// leases its state/credential files, publishes credentials atomically,
    /// pumps the existing transport and releases the service after admission stops.</summary>
    public sealed class AgentSimulationPlayerHost : IDisposable
    {
        private readonly AgentGameService _service;
        private readonly AgentGameHttpTransport _transport;
        private readonly FileStream _stateLease, _connectionLease;
        private readonly string _connectionFile;
        private bool _disposed;

        internal AgentSimulationPlayerHost(AgentGameService service, AgentGameHttpTransport transport,
            string connectionFile = null, FileStream stateLease = null, FileStream connectionLease = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionFile = connectionFile; _stateLease = stateLease; _connectionLease = connectionLease;
        }

        public static AgentSimulationPlayerHost Start(AgentSimulationHostSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            AgentSimulationPackageManifest manifest = AgentSimulationPackage.LoadVerified(settings.PackageRoot);
            if (manifest.engineVersion != Application.unityVersion)
                throw new InvalidDataException("Frozen package engine version differs from the running player.");
            Directory.CreateDirectory(settings.StateDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(settings.ConnectionFile));
            FileStream state = null, connection = null;
            AgentGameService service = null;
            AgentGameHttpTransport transport = null;
            try
            {
                state = new FileStream(Path.Combine(settings.StateDirectory, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                connection = new FileStream(settings.ConnectionFile + ".owner.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                service = new AgentGameService(settings.StateDirectory, manifest.buildId);
                transport = new AgentGameHttpTransport(service.HandleJson, AgentGameProtocol.Version);
                transport.Start(settings.Port);
                int processId;
                using (Process process = Process.GetCurrentProcess()) processId = process.Id;
                var payload = new AgentSimulationConnection
                {
                    protocolVersion = AgentGameProtocol.Version, endpoint = transport.Endpoint,
                    bearerToken = transport.BearerToken, gatewayInstanceId = transport.GatewayInstanceId,
                    processId = processId, startedUtc = DateTime.UtcNow.ToString("O"),
                    hostKind = "dedicated-player", buildId = manifest.buildId, stateDirectory = settings.StateDirectory
                };
                WriteAtomic(settings.ConnectionFile, JsonUtility.ToJson(payload, true));
                return new AgentSimulationPlayerHost(service, transport, settings.ConnectionFile, state, connection);
            }
            catch
            {
                try { transport?.Dispose(); }
                finally { try { service?.Dispose(); } finally { state?.Dispose(); connection?.Dispose(); } }
                throw;
            }
        }

        public void Pump()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AgentSimulationPlayerHost));
            if (!_transport.IsRunning) return;
            _transport.ProcessPending(4);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _transport.Dispose(); }
            finally
            {
                try { _service.Dispose(); }
                finally
                {
                    try { RemoveOwnedConnection(); }
                    finally { _stateLease?.Dispose(); _connectionLease?.Dispose(); }
                }
            }
        }

        private void RemoveOwnedConnection()
        {
            if (string.IsNullOrEmpty(_connectionFile) || !File.Exists(_connectionFile)) return;
            try
            {
                AgentSimulationConnection saved = JsonUtility.FromJson<AgentSimulationConnection>(File.ReadAllText(_connectionFile));
                if (saved.gatewayInstanceId == _transport.GatewayInstanceId) File.Delete(_connectionFile);
            }
            catch (Exception) { UnityEngine.Debug.LogWarning("[AgentPlayer] Stopped; stale connection cleanup was unavailable."); }
        }

        internal static void WriteAtomic(string path, string text)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    [Serializable]
    internal sealed class AgentSimulationConnection
    {
        public string protocolVersion, endpoint, bearerToken, gatewayInstanceId, startedUtc;
        public string hostKind, buildId, stateDirectory;
        public int processId;
    }
}
