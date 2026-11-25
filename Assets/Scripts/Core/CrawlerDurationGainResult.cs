using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>One resident's actual Duration gain, captured before later color triggers.</summary>
    public readonly struct CrawlerDurationChange
    {
        public int CrawlerRuntimeId { get; }
        public string CrawlerContentId { get; }
        public int Amount { get; }
        public int RemainingTriggers { get; }

        internal CrawlerDurationChange(
            int crawlerRuntimeId, string crawlerContentId, int amount, int remainingTriggers)
        {
            CrawlerRuntimeId = crawlerRuntimeId;
            CrawlerContentId = crawlerContentId;
            Amount = amount;
            RemainingTriggers = remainingTriggers;
        }
    }

    /// <summary>
    /// Immutable authority receipt for one Duration effect. Resident gains and the
    /// one-use summon reserve have different recipients and remain separate facts.
    /// </summary>
    public sealed class CrawlerDurationGainResult
    {
        public IReadOnlyList<CrawlerDurationChange> CrawlerChanges { get; }
        public int ReserveGained { get; }
        public int ResultingReserve { get; }
        public int TotalApplied { get; }

        internal CrawlerDurationGainResult(
            List<CrawlerDurationChange> crawlerChanges, int reserveGained, int resultingReserve)
        {
            CrawlerChanges = crawlerChanges.AsReadOnly();
            ReserveGained = reserveGained;
            ResultingReserve = resultingReserve;
            int total = reserveGained;
            for (int i = 0; i < crawlerChanges.Count; i++)
                total += crawlerChanges[i].Amount;
            TotalApplied = total;
        }
    }
}
