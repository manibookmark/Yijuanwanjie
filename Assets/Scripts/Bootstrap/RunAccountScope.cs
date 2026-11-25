using System;
using System.IO;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns the single in-memory account reference used by an active Run across
    /// map, combat and floor lifetimes. A new Run object, save slot or GameSession
    /// replaces the scope; consumers must pass only their actual matching Run.
    /// Initial loading stays caller-owned and runs once, never before a cache hit.
    /// No currency is reconciled or credited here: existing account transactions
    /// and persistence keep their full account object rather than stale copies.
    /// </summary>
    public static class RunAccountScope
    {
        private static DungeonRunSession _run;
        private static GameSession _gameSession;
        private static string _savePath;
        private static MetaProgression _account;

        public static MetaProgression Resolve(DungeonRunSession run,
            string savePath, Func<MetaProgression> load)
        {
            if (load == null) throw new ArgumentNullException(nameof(load));
            // Independent battles and legacy maps keep their former loading
            // contract without borrowing or replacing another active Run's account.
            if (run == null) return RequireAccount(load());
            if (string.IsNullOrWhiteSpace(savePath))
                throw new ArgumentException("Run account scope requires a save path.", nameof(savePath));

            string normalizedPath = Path.GetFullPath(savePath);
            GameSession session = GameSession.Current;
            if (ReferenceEquals(_run, run) && ReferenceEquals(_gameSession, session) &&
                string.Equals(_savePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                return _account;

            Clear();
            MetaProgression account = RequireAccount(load());
            _run = run;
            _gameSession = session;
            _savePath = normalizedPath;
            _account = account;
            return account;
        }

        /// <summary>Explicit scope teardown; never modifies the account or its wallet.</summary>
        public static void Clear()
        {
            _run = null;
            _gameSession = null;
            _savePath = null;
            _account = null;
        }

        private static MetaProgression RequireAccount(MetaProgression account)
            => account ?? throw new InvalidOperationException("The Run account loader returned no account.");
    }
}
