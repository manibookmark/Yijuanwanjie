using System;
using System.IO;
using UnityEngine;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Declares host admission and the immutable package-input location.
    /// The package owner verifies input hashes before opening a dedicated service.</summary>
    public static class AgentGameHostEnvironment
    {
        public static bool IsDedicatedPlayer
        {
            get
            {
#if WANJIE_SIMULATION_HOST
                return !Application.isEditor;
#else
                return false;
#endif
            }
        }

        internal static string HostKind(bool persistenceEnabled)
            => IsDedicatedPlayer ? "dedicated-player" : Application.isEditor
                ? persistenceEnabled ? "editor-durable" : "editor-memory"
                : persistenceEnabled ? "player-durable" : "player-memory";

        internal static void RequireFormalHost()
        {
            if (Application.isEditor)
            {
                if (Application.isPlaying)
                    throw new InvalidOperationException("Editor Agent sessions require stable Edit mode.");
            }
            else if (!IsDedicatedPlayer)
                throw new InvalidOperationException("Agent sessions require the dedicated simulation build profile.");
        }

        internal static string ReadHistoricalRecipe(string authorRelativePath)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string path = IsDedicatedPlayer
                ? ResolvePackageRecipe(root, authorRelativePath)
                : Path.Combine(root, authorRelativePath);
            return File.ReadAllText(path);
        }

        internal static string ResolvePackageRecipe(string packageRoot, string authorRelativePath)
        {
            if (string.IsNullOrWhiteSpace(packageRoot) || string.IsNullOrWhiteSpace(authorRelativePath) ||
                Path.IsPathRooted(authorRelativePath) || authorRelativePath.Contains(".."))
                throw new ArgumentException("A bounded relative historical-account recipe is required.");
            string normalized = authorRelativePath.Replace('\\', '/');
            const string prefix = "tools/simulation/account-recipes/";
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal) ||
                normalized.Substring(prefix.Length).Contains("/"))
                throw new ArgumentException("Recipe must be a declared account-recipes author file.");
            return Path.Combine(Path.GetFullPath(packageRoot), "SimulationInputs", "account-recipes", normalized.Substring(prefix.Length));
        }
    }
}
