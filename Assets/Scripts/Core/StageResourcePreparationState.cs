using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum StageResourcePreparationPhase { Empty, Preparing, Ready, Failed }

    /// <summary>
    /// Owns one generation's completion contract. Unity requests and retained
    /// objects belong to the runtime loader; late callbacks cannot publish Ready.
    /// </summary>
    public sealed class StageResourcePreparationState
    {
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.Ordinal);
        public string DungeonContentId { get; private set; }
        public int Generation { get; private set; }
        public StageResourcePreparationPhase Phase { get; private set; }
        public int TotalCount { get; private set; }
        public int CompletedCount => TotalCount - _pending.Count;
        public string Error { get; private set; }

        public bool IsReadyFor(string dungeonContentId) => Phase == StageResourcePreparationPhase.Ready &&
            string.Equals(DungeonContentId, dungeonContentId, StringComparison.Ordinal);

        public int Begin(StageResourceLoadPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            Generation++;
            DungeonContentId = plan.DungeonContentId;
            _pending.Clear();
            foreach (StageResourceLoadItem item in plan.Resources) _pending.Add(item.CacheKey);
            TotalCount = _pending.Count;
            Error = null;
            Phase = TotalCount == 0 ? StageResourcePreparationPhase.Ready : StageResourcePreparationPhase.Preparing;
            return Generation;
        }

        public bool TryMarkLoaded(int generation, string cacheKey)
        {
            if (generation != Generation || Phase != StageResourcePreparationPhase.Preparing ||
                !_pending.Remove(cacheKey ?? string.Empty)) return false;
            if (_pending.Count == 0) Phase = StageResourcePreparationPhase.Ready;
            return true;
        }

        public bool TryFail(int generation, string error)
        {
            if (generation != Generation || Phase != StageResourcePreparationPhase.Preparing) return false;
            Error = string.IsNullOrWhiteSpace(error) ? "关区资源预备失败。" : error;
            Phase = StageResourcePreparationPhase.Failed;
            return true;
        }

        public void Clear()
        {
            Generation++;
            DungeonContentId = null;
            Phase = StageResourcePreparationPhase.Empty;
            TotalCount = 0;
            Error = null;
            _pending.Clear();
        }
    }
}
