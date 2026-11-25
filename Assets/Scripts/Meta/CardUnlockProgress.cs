using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WanjieScroll.Core;

namespace WanjieScroll.Meta
{
    /// <summary>
    /// Owns permanent facts used by ordinary-card unlocks. A reached level is a
    /// maximum, never a sum. Cumulative Run facts are credited only above that
    /// Run's accepted watermark; thresholds and unaccepted Run state live elsewhere.
    /// </summary>
    public sealed class CardUnlockProgress
    {
        public int HighestPlayerLevel { get; private set; }
        private CardUnlockFactsSnapshot _lifetimeFacts = CardUnlockFactsSnapshot.Empty;
        private Dictionary<string, CardUnlockFactsSnapshot> _acceptedRuns =
            new Dictionary<string, CardUnlockFactsSnapshot>(StringComparer.Ordinal);
        private Dictionary<string, int> _crawlerHighestLevels =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public CardUnlockFactsSnapshot LifetimeFacts =>
            new CardUnlockFactsSnapshot(_lifetimeFacts.Counts);

        public int GetHighestPlayerLevel(string leaderCrawlerContentId = null)
        {
            if (string.IsNullOrEmpty(leaderCrawlerContentId)) return HighestPlayerLevel;
            ValidateCrawlerContentId(leaderCrawlerContentId);
            return _crawlerHighestLevels.TryGetValue(leaderCrawlerContentId, out int level) ? level : 0;
        }

        /// <summary>Only the frozen Run leader receives the per-crawler maximum;
        /// summoning a crawler during combat does not change this attribution.</summary>
        public bool RecordReachedLevel(int reachedLevel, string leaderCrawlerContentId = null)
        {
            if (reachedLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(reachedLevel));
            if (!string.IsNullOrEmpty(leaderCrawlerContentId)) ValidateCrawlerContentId(leaderCrawlerContentId);
            bool changed = reachedLevel > HighestPlayerLevel;
            HighestPlayerLevel = Math.Max(HighestPlayerLevel, reachedLevel);
            if (!string.IsNullOrEmpty(leaderCrawlerContentId) &&
                reachedLevel > GetHighestPlayerLevel(leaderCrawlerContentId))
            {
                _crawlerHighestLevels[leaderCrawlerContentId] = reachedLevel;
                changed = true;
            }
            return changed;
        }

        /// <summary>Credits an accepted cumulative Run exactly once; regressions are invalid.</summary>
        public void RecordAcceptedRun(string runId, CardUnlockFactsSnapshot cumulativeFacts)
        {
            CardUnlockFactsSnapshot current = CopyFacts(cumulativeFacts);
            CardUnlockFactsSnapshot projected = ProjectRun(runId, current);
            var accepted = new Dictionary<string, CardUnlockFactsSnapshot>(
                _acceptedRuns, StringComparer.Ordinal) { [runId] = current };
            _lifetimeFacts = projected;
            _acceptedRuns = accepted;
        }

        /// <summary>Queries permanent facts plus the unaccepted part of one Run without crediting it.</summary>
        public CardUnlockFactsSnapshot ProjectRun(string runId, CardUnlockFactsSnapshot current)
        {
            ValidateRunId(runId);
            current = CopyFacts(current);
            _acceptedRuns.TryGetValue(runId, out CardUnlockFactsSnapshot previous);
            var currentByKey = Index(current);
            var previousByKey = Index(previous ?? CardUnlockFactsSnapshot.Empty);
            foreach (var item in previousByKey)
                if (!currentByKey.TryGetValue(item.Key, out CardUnlockCount value) ||
                    value.Count < item.Value.Count)
                {
                    if (item.Value.Count > 0)
                        throw new InvalidOperationException("Accepted Run facts cannot decrease or disappear.");
                }

            var projected = Index(_lifetimeFacts);
            foreach (var item in currentByKey)
            {
                previousByKey.TryGetValue(item.Key, out CardUnlockCount before);
                int delta = item.Value.Count - before.Count;
                projected.TryGetValue(item.Key, out CardUnlockCount lifetime);
                if (delta == 0 && !projected.ContainsKey(item.Key)) continue;
                projected[item.Key] = new CardUnlockCount(item.Value.Metric,
                    lifetime.SourceContentId ?? item.Value.SourceContentId,
                    checked(lifetime.Count + delta));
            }
            return new CardUnlockFactsSnapshot(projected.Values);
        }

        public CardUnlockProgressSnapshot Capture()
        {
            var runs = new List<CardUnlockRunCheckpoint>();
            foreach (var item in _acceptedRuns)
                runs.Add(new CardUnlockRunCheckpoint(item.Key, item.Value));
            var levels = new List<CardUnlockCrawlerLevel>();
            foreach (var item in _crawlerHighestLevels)
                levels.Add(new CardUnlockCrawlerLevel(item.Key, item.Value));
            return new CardUnlockProgressSnapshot(HighestPlayerLevel, _lifetimeFacts.Counts, runs, levels);
        }

        /// <summary>Replaces a checkpoint; restoring never replays an earned fact.</summary>
        public void Restore(CardUnlockProgressSnapshot snapshot)
        {
            var validated = new CardUnlockProgressSnapshot(snapshot.HighestPlayerLevel,
                snapshot.LifetimeFacts.Counts, snapshot.AcceptedRuns, snapshot.CrawlerHighestLevels);
            var runs = new Dictionary<string, CardUnlockFactsSnapshot>(StringComparer.Ordinal);
            foreach (CardUnlockRunCheckpoint run in validated.AcceptedRuns)
                runs.Add(run.RunId, CopyFacts(run.Facts));
            CardUnlockFactsSnapshot lifetime = CopyFacts(validated.LifetimeFacts);
            var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (CardUnlockCrawlerLevel level in validated.CrawlerHighestLevels)
                levels.Add(level.CrawlerContentId, level.HighestPlayerLevel);
            HighestPlayerLevel = validated.HighestPlayerLevel;
            _lifetimeFacts = lifetime;
            _acceptedRuns = runs;
            _crawlerHighestLevels = levels;
        }

        internal static void ValidateCrawlerContentId(string crawlerContentId)
        {
            if (string.IsNullOrWhiteSpace(crawlerContentId))
                throw new ArgumentException("A stable leader crawler ID is required.", nameof(crawlerContentId));
            foreach (char value in crawlerContentId)
                if (char.IsWhiteSpace(value) || char.IsControl(value))
                    throw new ArgumentException("Crawler IDs cannot contain whitespace or control characters.", nameof(crawlerContentId));
        }

        internal static void ValidateRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId))
                throw new ArgumentException("An accepted Run requires a stable ID.", nameof(runId));
            foreach (char value in runId)
                if (char.IsWhiteSpace(value) || char.IsControl(value))
                    throw new ArgumentException("Run IDs cannot contain whitespace or control characters.", nameof(runId));
        }

        internal static CardUnlockFactsSnapshot CopyFacts(CardUnlockFactsSnapshot facts)
            => new CardUnlockFactsSnapshot((facts ?? throw new ArgumentNullException(nameof(facts))).Counts);

        internal static Dictionary<string, CardUnlockCount> Index(CardUnlockFactsSnapshot facts)
        {
            var result = new Dictionary<string, CardUnlockCount>(StringComparer.OrdinalIgnoreCase);
            foreach (CardUnlockCount fact in facts.Counts)
                result.Add(((int)fact.Metric) + ":" + fact.SourceContentId, fact);
            return result;
        }
    }

    public readonly struct CardUnlockProgressSnapshot
    {
        public int HighestPlayerLevel { get; }
        private readonly CardUnlockFactsSnapshot _lifetimeFacts;
        private readonly IReadOnlyList<CardUnlockRunCheckpoint> _acceptedRuns;
        private readonly IReadOnlyList<CardUnlockCrawlerLevel> _crawlerHighestLevels;

        public CardUnlockFactsSnapshot LifetimeFacts => _lifetimeFacts ?? CardUnlockFactsSnapshot.Empty;
        public IReadOnlyList<CardUnlockRunCheckpoint> AcceptedRuns =>
            _acceptedRuns ?? Array.Empty<CardUnlockRunCheckpoint>();
        public IReadOnlyList<CardUnlockCrawlerLevel> CrawlerHighestLevels =>
            _crawlerHighestLevels ?? Array.Empty<CardUnlockCrawlerLevel>();

        public CardUnlockProgressSnapshot(int highestPlayerLevel,
            IEnumerable<CardUnlockCount> lifetimeCounts = null,
            IEnumerable<CardUnlockRunCheckpoint> acceptedRuns = null,
            IEnumerable<CardUnlockCrawlerLevel> crawlerHighestLevels = null)
        {
            if (highestPlayerLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(highestPlayerLevel));
            HighestPlayerLevel = highestPlayerLevel;
            _lifetimeFacts = new CardUnlockFactsSnapshot(lifetimeCounts);
            var lifetime = CardUnlockProgress.Index(_lifetimeFacts);
            var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var runs = new List<CardUnlockRunCheckpoint>();
            foreach (CardUnlockRunCheckpoint source in acceptedRuns ?? Array.Empty<CardUnlockRunCheckpoint>())
            {
                if (source == null || !ids.Add(source.RunId))
                    throw new InvalidOperationException("Accepted Run checkpoints must be non-null and unique.");
                var run = new CardUnlockRunCheckpoint(source.RunId, source.Facts);
                foreach (var item in CardUnlockProgress.Index(run.Facts))
                {
                    totals.TryGetValue(item.Key, out int total);
                    total = checked(total + item.Value.Count);
                    if (total > 0 && (!lifetime.TryGetValue(item.Key, out CardUnlockCount fact) ||
                        fact.Count < total))
                        throw new InvalidOperationException("Permanent facts cannot omit accepted Run credits.");
                    totals[item.Key] = total;
                }
                runs.Add(run);
            }
            runs.Sort((left, right) => StringComparer.Ordinal.Compare(left.RunId, right.RunId));
            _acceptedRuns = new ReadOnlyCollection<CardUnlockRunCheckpoint>(runs);
            var levels = new List<CardUnlockCrawlerLevel>();
            var crawlerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardUnlockCrawlerLevel level in crawlerHighestLevels ?? Array.Empty<CardUnlockCrawlerLevel>())
            {
                if (level == null || !crawlerIds.Add(level.CrawlerContentId) ||
                    level.HighestPlayerLevel > highestPlayerLevel)
                    throw new InvalidOperationException("Crawler level maxima must be unique and cannot exceed the account maximum.");
                levels.Add(new CardUnlockCrawlerLevel(level.CrawlerContentId, level.HighestPlayerLevel));
            }
            levels.Sort((left, right) => StringComparer.Ordinal.Compare(left.CrawlerContentId, right.CrawlerContentId));
            _crawlerHighestLevels = new ReadOnlyCollection<CardUnlockCrawlerLevel>(levels);
        }
    }

    /// <summary>Immutable maximum reached with one crawler leading a Run.</summary>
    public sealed class CardUnlockCrawlerLevel
    {
        public string CrawlerContentId { get; }
        public int HighestPlayerLevel { get; }

        public CardUnlockCrawlerLevel(string crawlerContentId, int highestPlayerLevel)
        {
            CardUnlockProgress.ValidateCrawlerContentId(crawlerContentId);
            if (highestPlayerLevel < 1) throw new ArgumentOutOfRangeException(nameof(highestPlayerLevel));
            CrawlerContentId = crawlerContentId;
            HighestPlayerLevel = highestPlayerLevel;
        }
    }

    /// <summary>Detached accepted watermark; immutable facts never credit a Run by themselves.</summary>
    public sealed class CardUnlockRunCheckpoint
    {
        public string RunId { get; }
        public CardUnlockFactsSnapshot Facts { get; }

        public CardUnlockRunCheckpoint(string runId, CardUnlockFactsSnapshot facts)
        {
            CardUnlockProgress.ValidateRunId(runId);
            RunId = runId;
            Facts = CardUnlockProgress.CopyFacts(facts);
        }
    }
}
