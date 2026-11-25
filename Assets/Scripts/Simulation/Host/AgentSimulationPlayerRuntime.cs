using System;
using System.IO;
using UnityEngine;

namespace WanjieScroll.Simulation.Host
{
    /// <summary>Unity lifecycle adapter for the dedicated simulation host. The
    /// build-only entry creates no ordinary scene/UI/audio or user-save services.</summary>
    public sealed class AgentSimulationPlayerRuntime : MonoBehaviour
    {
        private AgentSimulationPlayerHost _host;

#if WANJIE_SIMULATION_HOST
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor) return;
            AgentSimulationHostSettings settings = null;
            try
            {
                string packageRoot = Path.GetDirectoryName(Application.dataPath);
                settings = AgentSimulationHostSettings.Parse(Environment.GetCommandLineArgs(), packageRoot);
                AgentSimulationPlayerHost host = AgentSimulationPlayerHost.Start(settings);
                var root = new GameObject("[AgentSimulationPlayer]");
                var runtime = root.AddComponent<AgentSimulationPlayerRuntime>();
                runtime.Attach(host);
                DontDestroyOnLoad(root);
                Application.runInBackground = true;
                Application.targetFrameRate = 30;
                Debug.Log("[AgentPlayer] Dedicated gateway ready. Credentials remain in its local connection file.");
            }
            catch (Exception exception)
            {
                if (settings != null)
                {
                    try { AgentSimulationPlayerHost.WriteAtomic(settings.ConnectionFile + ".failure.json",
                        "{\"success\":false,\"error\":\"" + exception.GetType().Name + "\"}"); } catch { }
                }
                Debug.LogError("[AgentPlayer] Startup failed: " + exception.GetType().Name + ": " + exception.Message);
                Application.Quit(2);
            }
        }
#endif

        internal void Attach(AgentSimulationPlayerHost host)
        {
            if (_host != null || host == null) throw new InvalidOperationException("Host must be attached once.");
            _host = host;
        }
        private void Update() => _host?.Pump();
        private void OnApplicationQuit() => StopOwnedHost();
        private void OnDestroy() => StopOwnedHost();
        private void StopOwnedHost()
        {
            AgentSimulationPlayerHost owned = _host; _host = null; owned?.Dispose();
        }
    }
}
