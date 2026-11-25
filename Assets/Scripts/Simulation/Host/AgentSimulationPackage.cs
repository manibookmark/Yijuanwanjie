using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WanjieScroll.Simulation.Host
{
    /// <summary>
    /// Owns the immutable installed simulation package contract. Source inputs
    /// explain provenance; installed file hashes and the derived build ID bind
    /// the compiled rules, engine files and frozen recipes actually executed.
    /// No verifier consults the changing development checkout.
    /// </summary>
    public static class AgentSimulationPackage
    {
        public const string ManifestRelativePath = "SimulationInputs/manifest.json";
        public const int SchemaVersion = 1;

        public static AgentSimulationPackageManifest LoadVerified(string packageRoot)
        {
            packageRoot = Path.GetFullPath(packageRoot);
            string path = ResolveFile(packageRoot, ManifestRelativePath);
            var manifest = JsonUtility.FromJson<AgentSimulationPackageManifest>(File.ReadAllText(path, Encoding.UTF8));
            ValidateManifest(manifest);
            string prefix = packageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var installedFiles = new HashSet<string>(Directory.GetFiles(packageRoot, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetFullPath(file).Substring(prefix.Length).Replace('\\', '/'))
                .Where(file => file != ManifestRelativePath), StringComparer.OrdinalIgnoreCase);
            if (!installedFiles.SetEquals(manifest.files.Select(file => file.path)))
                throw new InvalidDataException("Installed simulation file set differs from the frozen ledger.");
            foreach (AgentSimulationPackageFile file in manifest.files)
            {
                string installedPath = ResolveFile(packageRoot, file.path);
                var info = new FileInfo(installedPath);
                if (!info.Exists || info.Length != file.bytes || HashFile(installedPath) != file.sha256)
                    throw new InvalidDataException("Installed simulation package differs: " + file.path);
            }
            return manifest;
        }

        public static string ComputeBuildId(AgentSimulationPackageManifest manifest)
        {
            if (manifest == null || manifest.files == null) throw new ArgumentNullException(nameof(manifest));
            var text = new StringBuilder("wanjie-agent-player.v1\n");
            text.Append(manifest.engineVersion).Append('\n').Append(manifest.sourceDigest).Append('\n');
            foreach (AgentSimulationPackageFile file in manifest.files.OrderBy(value => value.path, StringComparer.Ordinal))
                text.Append(file.path).Append('\t').Append(file.bytes.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(file.sha256).Append('\n');
            return HashText(text.ToString());
        }

        public static string ComputeSourceDigest(IEnumerable<AgentSimulationPackageFile> files) => HashText(
            string.Join("\n", files.OrderBy(file => file.path, StringComparer.Ordinal).Select(file =>
                file.path + "\t" + file.bytes.ToString(CultureInfo.InvariantCulture) + "\t" + file.sha256)));

        public static string HashFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(stream));
        }

        public static string HashText(string value)
        {
            using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }

        internal static string ResolveFile(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') ||
                relative.Split('/').Any(segment => segment == ".." || segment == "." || segment.Length == 0))
                throw new InvalidDataException("Invalid simulation package relative path.");
            string path = Path.GetFullPath(Path.Combine(root, relative));
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Simulation package path escapes its root.");
            return path;
        }

        private static void ValidateManifest(AgentSimulationPackageManifest manifest)
        {
            if (manifest == null || manifest.schemaVersion != SchemaVersion || !IsHash(manifest.buildId) ||
                !IsHash(manifest.sourceDigest) || string.IsNullOrEmpty(manifest.engineVersion) ||
                string.IsNullOrEmpty(manifest.playerExecutable) || manifest.files == null || manifest.files.Count == 0 ||
                manifest.sourceFiles == null || manifest.sourceFiles.Count == 0)
                throw new InvalidDataException("Invalid simulation package manifest.");
            var paths = ValidateLedger(manifest.files);
            ValidateLedger(manifest.sourceFiles);
            if (paths.Contains(ManifestRelativePath) || ComputeSourceDigest(manifest.sourceFiles) != manifest.sourceDigest)
                throw new InvalidDataException("Source provenance or installed simulation ledger is inconsistent.");
            if (!paths.Contains(manifest.playerExecutable) || !manifest.files.Any(file => file.path.StartsWith("SimulationInputs/account-recipes/", StringComparison.Ordinal)))
                throw new InvalidDataException("Simulation executable or frozen account recipes missing.");
            if (ComputeBuildId(manifest) != manifest.buildId)
                throw new InvalidDataException("Simulation build identity differs from its installed file ledger.");
        }

        private static HashSet<string> ValidateLedger(IEnumerable<AgentSimulationPackageFile> files)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string prior = null;
            foreach (AgentSimulationPackageFile file in files)
            {
                if (file == null || file.bytes < 0 || !IsHash(file.sha256) || !paths.Add(file.path) ||
                    prior != null && string.CompareOrdinal(prior, file.path) >= 0)
                    throw new InvalidDataException("Simulation file ledger must contain unique, valid files in ordinal path order.");
                ResolveFile(Path.GetTempPath(), file.path); // Validate syntax without reading source checkout files.
                prior = file.path;
            }
            return paths;
        }

        private static bool IsHash(string value) => value != null && value.Length == 64 &&
            value.All(character => character >= '0' && character <= '9' || character >= 'a' && character <= 'f');
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    [Serializable]
    public sealed class AgentSimulationPackageManifest
    {
        public int schemaVersion;
        public string buildId, engineVersion, sourceDigest, playerExecutable;
        public List<AgentSimulationPackageFile> sourceFiles = new List<AgentSimulationPackageFile>();
        public List<AgentSimulationPackageFile> files = new List<AgentSimulationPackageFile>();
    }

    [Serializable]
    public sealed class AgentSimulationPackageFile
    {
        public string path, sha256;
        public long bytes;
    }
}
