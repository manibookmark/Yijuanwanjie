using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace WanjieScroll.Simulation.Host
{
    /// <summary>Validated launch intent. Dedicated state and credential paths
    /// are mandatory; no fallback may touch a player's persistentDataPath.</summary>
    public sealed class AgentSimulationHostSettings
    {
        public const int DefaultPort = 17642;
        public string PackageRoot { get; }
        public string StateDirectory { get; }
        public string ConnectionFile { get; }
        public int Port { get; }

        private AgentSimulationHostSettings(string package, string state, string connection, int port)
        { PackageRoot = package; StateDirectory = state; ConnectionFile = connection; Port = port; }

        public static AgentSimulationHostSettings Parse(string[] arguments, string packageRoot)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < arguments.Length; i++)
            {
                string key = arguments[i];
                if (key != "-agentStateDirectory" && key != "-agentConnectionFile" && key != "-agentPort") continue;
                if (values.ContainsKey(key) || ++i >= arguments.Length || string.IsNullOrWhiteSpace(arguments[i]))
                    throw new ArgumentException("Missing or repeated simulation launch argument: " + key);
                values.Add(key, arguments[i]);
            }
            if (!values.TryGetValue("-agentStateDirectory", out string state) ||
                !values.TryGetValue("-agentConnectionFile", out string connection) ||
                !Path.IsPathRooted(state) || !Path.IsPathRooted(connection))
                throw new ArgumentException("Absolute dedicated state and connection paths are required.");
            int port = values.TryGetValue("-agentPort", out string number)
                ? int.Parse(number, NumberStyles.None, CultureInfo.InvariantCulture) : DefaultPort;
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            string package = Path.GetFullPath(packageRoot);
            state = Path.GetFullPath(state); connection = Path.GetFullPath(connection);
            if (Inside(state, package) || Inside(connection, package) || state.Equals(connection, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Mutable state and credentials must stay outside the frozen package.");
            return new AgentSimulationHostSettings(package, state, connection, port);
        }

        private static bool Inside(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}
