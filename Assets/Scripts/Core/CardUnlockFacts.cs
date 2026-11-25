using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WanjieScroll.Core
{
    public enum CardUnlockMetric
    {
        CardPlayed = 1,
        FoodCollected = 2,
        LightDestroyed = 3,
        ManaCollected = 4,
        EnemiesKilled = 5,
        CrawlerActionsTriggered = 6
    }

    /// <summary>Global preserves existing cumulative conditions; Run requires one current Run.</summary>
    public enum CardUnlockScope
    {
        Global = 0,
        Run = 1
    }

    /// <summary>Author-owned eligibility condition, independent of publication and reward cost.</summary>
    [Serializable]
    public sealed class CardUnlockCondition
    {
        public CardUnlockMetric metric;
        public CardUnlockScope scope;
        public int requiredCount;
        public List<string> sourceCardContentIds = new List<string>();

        // Unity may deserialize an omitted inline class as a zero-valued object.
        // An all-zero optional condition is absent; any authored field opts in
        // and must pass the full validator (including malformed partial fields).
        public bool IsConfigured => metric != 0 || scope != CardUnlockScope.Global || requiredCount != 0 ||
            (sourceCardContentIds != null && sourceCardContentIds.Count > 0);

        public string Validate()
        {
            if (!Enum.IsDefined(typeof(CardUnlockScope), scope))
                return "行为解锁需要合法累计范围。";
            if (!Enum.IsDefined(typeof(CardUnlockMetric), metric) || requiredCount < 1)
                return "行为解锁需要合法指标与正数阈值。";
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in sourceCardContentIds ?? new List<string>())
                if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || !ids.Add(id))
                    return "行为解锁来源必须为不重复的稳定卡牌ID。";
            if (metric == CardUnlockMetric.CardPlayed ? ids.Count == 0 : ids.Count != 0)
                return "出牌指标需要来源卡牌；世界拾取指标不能绑定卡牌。";
            return null;
        }

        public bool IsSatisfied(CardUnlockFactsSnapshot facts)
            => IsSatisfied(facts, null);

        /// <summary>
        /// Selects the authority snapshot for the authored scope. A missing current
        /// Run can never satisfy a Run condition, even with enough lifetime facts.
        /// </summary>
        public bool IsSatisfied(
            CardUnlockFactsSnapshot globalFacts, CardUnlockFactsSnapshot currentRunFacts)
        {
            if (Validate() != null) throw new InvalidOperationException(Validate());
            CardUnlockFactsSnapshot facts = scope == CardUnlockScope.Run ? currentRunFacts : globalFacts;
            if (facts == null) return false;
            long count = 0;
            foreach (CardUnlockCount fact in facts.Counts)
            {
                if (fact.Metric != metric) continue;
                if (metric != CardUnlockMetric.CardPlayed || sourceCardContentIds.Exists(id =>
                    string.Equals(id, fact.SourceContentId, StringComparison.OrdinalIgnoreCase)))
                    count += fact.Count;
                if (count >= requiredCount) return true;
            }
            return false;
        }
    }

    public readonly struct CardUnlockCount
    {
        public CardUnlockMetric Metric { get; }
        public string SourceContentId { get; }
        public int Count { get; }

        public CardUnlockCount(CardUnlockMetric metric, string sourceContentId, int count)
        {
            if (!Enum.IsDefined(typeof(CardUnlockMetric), metric))
                throw new ArgumentOutOfRangeException(nameof(metric));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (metric == CardUnlockMetric.CardPlayed)
            {
                if (string.IsNullOrWhiteSpace(sourceContentId) || sourceContentId != sourceContentId.Trim())
                    throw new ArgumentException("Card plays require a stable source ID.", nameof(sourceContentId));
            }
            else if (!string.IsNullOrEmpty(sourceContentId))
                throw new ArgumentException("World facts cannot masquerade as card plays.", nameof(sourceContentId));
            Metric = metric;
            SourceContentId = sourceContentId ?? string.Empty;
            Count = count;
        }
    }

    /// <summary>Validated detached facts; construction and restoration never credit an event.</summary>
    public sealed class CardUnlockFactsSnapshot
    {
        public static CardUnlockFactsSnapshot Empty { get; } = new CardUnlockFactsSnapshot();
        public IReadOnlyList<CardUnlockCount> Counts { get; }

        public CardUnlockFactsSnapshot(IEnumerable<CardUnlockCount> counts = null)
        {
            var copy = new List<CardUnlockCount>();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardUnlockCount value in counts ?? Array.Empty<CardUnlockCount>())
            {
                var validated = new CardUnlockCount(value.Metric, value.SourceContentId, value.Count);
                string key = ((int)value.Metric) + ":" + validated.SourceContentId;
                if (!keys.Add(key)) throw new InvalidOperationException("Duplicate card-unlock fact.");
                copy.Add(validated);
            }
            copy.Sort((left, right) => left.Metric != right.Metric
                ? left.Metric.CompareTo(right.Metric)
                : StringComparer.Ordinal.Compare(left.SourceContentId, right.SourceContentId));
            Counts = new ReadOnlyCollection<CardUnlockCount>(copy);
        }
    }

    /// <summary>
    /// Owns successful real-play, actual enemy deaths, actual crawler reactions and explicit world facts
    /// for one Run. Damage, card acquisition and restored statistics are not
    /// facts. Meta commits accepted deltas separately; restore replaces state.
    /// </summary>
    public sealed class RunCardUnlockFacts
    {
        private readonly Dictionary<string, CardUnlockCount> _counts =
            new Dictionary<string, CardUnlockCount>(StringComparer.OrdinalIgnoreCase);
        private int _harnessFactSuppressionDepth;

        public void RecordCardPlayed(string contentId)
            => Record(new CardUnlockCount(CardUnlockMetric.CardPlayed, contentId, 1));

        public void RecordWorldFact(CardUnlockMetric metric, int count = 1)
        {
            if (metric == CardUnlockMetric.CardPlayed || metric == CardUnlockMetric.CrawlerActionsTriggered)
                throw new ArgumentException("Use the authoritative successful combat-action boundary.", nameof(metric));
            Record(new CardUnlockCount(metric, string.Empty, count));
        }

        /// <summary>Only actual death boundaries credit enemies, never damage or presentation callbacks.</summary>
        internal void RecordEnemyKilled()
        {
            if (_harnessFactSuppressionDepth == 0)
                RecordWorldFact(CardUnlockMetric.EnemiesKilled);
        }

        /// <summary>One committed crawler reaction, after qualification and duration consumption.</summary>
        internal void RecordCrawlerActionTriggered()
        {
            if (_harnessFactSuppressionDepth == 0)
                Record(new CardUnlockCount(CardUnlockMetric.CrawlerActionsTriggered, string.Empty, 1));
        }

        /// <summary>Harness effects may resolve real damage but cannot become permanent unlock evidence.</summary>
        internal IDisposable SuppressCombatFactsForHarness()
        {
            _harnessFactSuppressionDepth++;
            return new HarnessFactScope(this);
        }

        private sealed class HarnessFactScope : IDisposable
        {
            private RunCardUnlockFacts _owner;
            internal HarnessFactScope(RunCardUnlockFacts owner) { _owner = owner; }
            public void Dispose()
            {
                if (_owner == null) return;
                _owner._harnessFactSuppressionDepth--;
                _owner = null;
            }
        }

        public CardUnlockFactsSnapshot Capture() => new CardUnlockFactsSnapshot(_counts.Values);

        public void Restore(CardUnlockFactsSnapshot snapshot)
        {
            var validated = new CardUnlockFactsSnapshot((snapshot ?? CardUnlockFactsSnapshot.Empty).Counts);
            _counts.Clear();
            foreach (CardUnlockCount value in validated.Counts)
                _counts.Add(Key(value), value);
        }

        private void Record(CardUnlockCount delta)
        {
            if (delta.Count == 0) return;
            string key = Key(delta);
            _counts.TryGetValue(key, out CardUnlockCount previous);
            int next = (int)Math.Min(int.MaxValue, (long)previous.Count + delta.Count);
            _counts[key] = new CardUnlockCount(delta.Metric, delta.SourceContentId, next);
        }

        private static string Key(CardUnlockCount value)
            => ((int)value.Metric) + ":" + value.SourceContentId;
    }
}
