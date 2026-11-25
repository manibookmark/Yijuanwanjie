using System;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>Live read-only facts for new offers; frozen offers keep their IDs.
    /// The account projects only the unaccepted delta from this Run.</summary>
    public sealed class CardUnlockRunContext
    {
        private readonly Func<CardUnlockFactsSnapshot> _capture;
        public string RunId { get; }
        public string LeaderCrawlerContentId { get; }

        public CardUnlockRunContext(string runId, Func<CardUnlockFactsSnapshot> capture,
            string leaderCrawlerContentId = null)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("Run ID is required.");
            RunId = runId;
            LeaderCrawlerContentId = ValidateLeaderContentId(leaderCrawlerContentId);
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        }

        public CardUnlockFactsSnapshot Capture()
            => _capture() ?? throw new InvalidOperationException("Run facts are missing.");

        public CardUnlockFactsSnapshot Project(MetaProgression account)
            => account.CardUnlocks.ProjectRun(RunId, Capture());

        public CardUnlockAcceptance Accept(int reachedLevel)
            => new CardUnlockAcceptance(RunId, reachedLevel, Capture(), LeaderCrawlerContentId);

        internal static string ValidateLeaderContentId(string contentId)
        {
            if (string.IsNullOrEmpty(contentId)) return string.Empty;
            foreach (char value in contentId)
                if (char.IsWhiteSpace(value) || char.IsControl(value))
                    throw new ArgumentException("A stable leader crawler ID cannot contain whitespace or control characters.", nameof(contentId));
            return contentId;
        }
    }

    public sealed class CardUnlockAcceptance
    {
        public EventUnlockRunConclusion Conclusion { get; }
        public static CardUnlockAcceptance FromRun(DungeonRunSession run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            EventUnlockRunConclusion conclusion = EventUnlockRunConclusion.None;
            if (run.TryGetConclusion(out DungeonRunResultSnapshot result))
            {
                if (result.Status == DungeonRunResultStatus.Victory) conclusion = EventUnlockRunConclusion.Victory;
                else if (result.Status == DungeonRunResultStatus.Defeat) conclusion = EventUnlockRunConclusion.Defeat;
                else throw new InvalidOperationException("Unknown whole-Run conclusion.");
            }
            return new CardUnlockAcceptance(run.RunId, run.CombatProgression.CurrentLevel,
                run.RunProgression.CardUnlockFacts.Capture(), run.LeaderCrawlerContentId, conclusion,
                EvoOfferingRunProof.FromCompletedHistory(run.RunId, run.DungeonContentId,
                    run.CaptureCompletedFloorContentIds()));
        }

        public string RunId { get; }
        public int ReachedLevel { get; }
        public CardUnlockFactsSnapshot Facts { get; }
        public string LeaderCrawlerContentId { get; }
        public EvoOfferingRunProof EvoProof { get; }

        public CardUnlockAcceptance(string runId, int reachedLevel, CardUnlockFactsSnapshot facts,
            string leaderCrawlerContentId = null, EventUnlockRunConclusion conclusion = EventUnlockRunConclusion.None,
            EvoOfferingRunProof evoProof = null)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("Run ID is required.");
            if (reachedLevel < 1) throw new ArgumentOutOfRangeException(nameof(reachedLevel));
            RunId = runId;
            ReachedLevel = reachedLevel;
            LeaderCrawlerContentId = CardUnlockRunContext.ValidateLeaderContentId(leaderCrawlerContentId);
            if (!Enum.IsDefined(typeof(EventUnlockRunConclusion), conclusion))
                throw new ArgumentOutOfRangeException(nameof(conclusion));
            Conclusion = conclusion;
            if (evoProof != null && evoProof.RunId != runId)
                throw new ArgumentException("Accepted floor proof belongs to a different Run.", nameof(evoProof));
            EvoProof = evoProof;
            Facts = new CardUnlockFactsSnapshot((facts ?? throw new ArgumentNullException(nameof(facts))).Counts);
        }
    }
}
