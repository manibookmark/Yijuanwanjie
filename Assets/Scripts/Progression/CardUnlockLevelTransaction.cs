using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WanjieScroll.Core;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    public sealed class CardUnlockBehaviorRequirement
    {
        private readonly CardUnlockCondition _condition;
        public string CardContentId { get; }
        public CardUnlockScope Scope => _condition.scope;

        public CardUnlockBehaviorRequirement(string cardContentId, CardUnlockCondition condition)
        {
            if (string.IsNullOrWhiteSpace(cardContentId)) throw new ArgumentException("Card ID is required.");
            if (condition == null || condition.Validate() != null)
                throw new ArgumentException("A valid behavior condition is required.");
            CardContentId = cardContentId;
            _condition = new CardUnlockCondition { metric = condition.metric, scope = condition.scope,
                requiredCount = condition.requiredCount,
                sourceCardContentIds = new List<string>(condition.sourceCardContentIds ?? new List<string>()) };
        }

        public bool IsSatisfied(CardUnlockFactsSnapshot facts) => _condition.IsSatisfied(facts);
        public bool IsSatisfied(CardUnlockFactsSnapshot globalFacts, CardUnlockFactsSnapshot currentRunFacts)
            => _condition.IsSatisfied(globalFacts, currentRunFacts);
    }

    public sealed class CardUnlockRequirements
    {
        public IReadOnlyList<CardUnlockLevelRequirement> Levels { get; }
        public IReadOnlyList<CardUnlockBehaviorRequirement> Behaviors { get; }

        public CardUnlockRequirements(IEnumerable<CardUnlockLevelRequirement> levels,
            IEnumerable<CardUnlockBehaviorRequirement> behaviors)
        {
            var levelList = new List<CardUnlockLevelRequirement>(levels ?? Array.Empty<CardUnlockLevelRequirement>());
            var behaviorList = new List<CardUnlockBehaviorRequirement>(behaviors ?? Array.Empty<CardUnlockBehaviorRequirement>());
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in levelList)
                if (item == null || !ids.Add(item.CardContentId)) throw new ArgumentException("Duplicate or null unlock condition.");
            foreach (var item in behaviorList)
                if (item == null || !ids.Add(item.CardContentId)) throw new ArgumentException("Duplicate or null unlock condition.");
            Levels = levelList.AsReadOnly();
            Behaviors = behaviorList.AsReadOnly();
        }
    }

    /// <summary>An immutable level condition projected from published author content.</summary>
    public sealed class CardUnlockLevelRequirement
    {
        public string CardContentId { get; }
        public int RequiredPlayerLevel { get; }
        public string RequiredCrawlerContentId { get; }

        public CardUnlockLevelRequirement(string cardContentId, int requiredPlayerLevel,
            string requiredCrawlerContentId = null)
        {
            if (string.IsNullOrWhiteSpace(cardContentId))
                throw new ArgumentException("A stable card ID is required.", nameof(cardContentId));
            if (requiredPlayerLevel < 1)
                throw new ArgumentOutOfRangeException(nameof(requiredPlayerLevel));
            CardContentId = cardContentId.Trim();
            RequiredPlayerLevel = requiredPlayerLevel;
            RequiredCrawlerContentId = requiredCrawlerContentId ?? string.Empty;
            foreach (char value in RequiredCrawlerContentId)
                if (char.IsWhiteSpace(value) || char.IsControl(value))
                    throw new ArgumentException("A stable crawler ID cannot contain whitespace or control characters.", nameof(requiredCrawlerContentId));
        }

        /// <summary>Current-Run evidence is read-only. A constrained condition
        /// never receives another leader's level or the account-wide peak.</summary>
        public bool IsSatisfied(CardUnlockProgress progress, int currentRunLevel = 0,
            string currentLeaderCrawlerContentId = null)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            int reached = progress.GetHighestPlayerLevel(RequiredCrawlerContentId);
            if (string.IsNullOrEmpty(RequiredCrawlerContentId) ||
                string.Equals(RequiredCrawlerContentId, currentLeaderCrawlerContentId, StringComparison.OrdinalIgnoreCase))
                reached = Math.Max(reached, currentRunLevel);
            return reached >= RequiredPlayerLevel;
        }
    }

    /// <summary>Durable outcome, returned only after the enclosing account/Run save succeeds.</summary>
    public sealed class CardUnlockLevelReceipt
    {
        public int PreviousHighestPlayerLevel { get; }
        public int HighestPlayerLevel { get; }
        public IReadOnlyList<string> NewlyUnlockedCardContentIds { get; }

        internal CardUnlockLevelReceipt(int previousLevel, int highestLevel, List<string> cards)
        {
            PreviousHighestPlayerLevel = previousLevel;
            HighestPlayerLevel = highestLevel;
            NewlyUnlockedCardContentIds = new ReadOnlyCollection<string>(cards.ToArray());
        }
    }

    /// <summary>
    /// Commits accepted level/behavior facts and eligibility with a caller-owned
    /// Run checkpoint. No card entity, content lookup, storage or scene is owned here.
    /// The save callback must throw on failure; the Run rollback callback restores
    /// the same pre-acceptance checkpoint. Pause saves do not use this transaction.
    /// Scope selection belongs to the typed condition. This transaction only
    /// commits its permanent eligibility through the existing save/rollback boundary.
    /// </summary>
    public static class CardUnlockLevelTransaction
    {
        /// <summary>
        /// Allows formal real-play and resume adapters to request an immediate durable
        /// checkpoint only when a not-yet-unlocked Run condition is met. This read-only
        /// query neither credits lifetime facts nor changes a frozen reward offer.
        /// </summary>
        public static bool HasPendingRunScopedUnlock(
            MetaProgression account, CardUnlockAcceptance currentRun, CardUnlockRequirements requirements)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (currentRun == null) throw new ArgumentNullException(nameof(currentRun));
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            foreach (CardUnlockBehaviorRequirement condition in requirements.Behaviors)
                if (condition.Scope == CardUnlockScope.Run &&
                    !account.IsCardUnlocked(condition.CardContentId) &&
                    condition.IsSatisfied(null, currentRun.Facts))
                    return true;
            return false;
        }

        /// <summary>
        /// Grants only permanent Run-scoped eligibility at a real-play/resume boundary.
        /// Lifetime facts and their accepted watermarks remain owned by normal result
        /// acceptance, so saving an existing pre-combat checkpoint stays valid. The
        /// formal adapter must exclude editor harnesses; this transaction credits no
        /// plays itself, and never mutates a Run or an already frozen reward offer.
        /// </summary>
        public static bool TryRefreshRunScopedEligibility(
            MetaProgression account, CardUnlockAcceptance currentRun, CardUnlockRequirements requirements,
            Action persist, out CardUnlockLevelReceipt receipt, out string error)
        {
            receipt = null;
            error = null;
            List<string> beforeCards = account?.unlockedCards;
            string[] beforeCardIds = beforeCards?.ToArray();
            try
            {
                if (account == null) throw new ArgumentNullException(nameof(account));
                if (currentRun == null) throw new ArgumentNullException(nameof(currentRun));
                if (requirements == null) throw new ArgumentNullException(nameof(requirements));
                if (persist == null) throw new ArgumentNullException(nameof(persist));
                var granted = new List<string>();
                foreach (CardUnlockBehaviorRequirement condition in requirements.Behaviors)
                    if (condition.Scope == CardUnlockScope.Run &&
                        condition.IsSatisfied(null, currentRun.Facts) &&
                        account.TryUnlockCard(condition.CardContentId))
                        granted.Add(condition.CardContentId);
                // A repeat restore or a non-threshold play creates no extra save and
                // cannot advance the independently accepted lifetime watermark.
                if (granted.Count > 0) persist();
                int highest = account.CardUnlocks.HighestPlayerLevel;
                receipt = new CardUnlockLevelReceipt(highest, highest, granted);
                return true;
            }
            catch (Exception exception)
            {
                if (account != null)
                {
                    if (beforeCards != null)
                    {
                        beforeCards.Clear();
                        beforeCards.AddRange(beforeCardIds);
                    }
                    account.unlockedCards = beforeCards;
                }
                error = "Run-scoped card eligibility refresh failed: " + exception.Message;
                return false;
            }
        }

        public static bool TryCommit(MetaProgression account, CardUnlockAcceptance accepted,
            CardUnlockRequirements requirements, Action persist, Action rollbackRun,
            out CardUnlockLevelReceipt receipt, out string error)
        {
            return TryCommit(account, accepted?.ReachedLevel ?? 0, requirements?.Levels,
                persist, rollbackRun, out receipt, out error, accepted, requirements?.Behaviors);
        }

        // Retained for callers that only have a level milestone (no Run facts).
        public static bool TryCommit(MetaProgression account, int reachedLevel,
            IEnumerable<CardUnlockLevelRequirement> requirements, Action persist,
            Action rollbackRun, out CardUnlockLevelReceipt receipt, out string error,
            CardUnlockAcceptance accepted = null,
            IEnumerable<CardUnlockBehaviorRequirement> behaviorRequirements = null)
        {
            receipt = null;
            error = null;
            if (account?.EventUnlocks.IsCommitting == true)
            { error = "Event facts are already committing."; return false; }
            CardUnlockProgressSnapshot beforeProgress = account?.CardUnlocks.Capture() ?? default;
            EventUnlockProgressSnapshot beforeEvents = account?.EventUnlocks.Capture() ?? default;
            List<string> beforeCards = account?.unlockedCards;
            string[] beforeCardIds = beforeCards?.ToArray();
            try
            {
                if (account == null) throw new ArgumentNullException(nameof(account));
                if (reachedLevel < 1) throw new ArgumentOutOfRangeException(nameof(reachedLevel));
                if (requirements == null) throw new ArgumentNullException(nameof(requirements));
                if (persist == null) throw new ArgumentNullException(nameof(persist));
                if (rollbackRun == null) throw new ArgumentNullException(nameof(rollbackRun));
                var conditions = new List<CardUnlockLevelRequirement>();
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (CardUnlockLevelRequirement condition in requirements)
                {
                    if (condition == null || !ids.Add(condition.CardContentId))
                        throw new InvalidOperationException("Level unlock conditions contain a null or duplicate card.");
                    conditions.Add(condition);
                }
                var behaviors = new List<CardUnlockBehaviorRequirement>();
                foreach (var condition in behaviorRequirements ?? Array.Empty<CardUnlockBehaviorRequirement>())
                {
                    if (condition == null || !ids.Add(condition.CardContentId))
                        throw new InvalidOperationException("Behavior unlock conditions contain a null or duplicate card.");
                    behaviors.Add(condition);
                }

                if (accepted != null && accepted.ReachedLevel != reachedLevel)
                    throw new InvalidOperationException("Accepted Run level differs from the committed level.");
                var granted = new List<string>();
                Action commitAndPersist = () =>
                {
                    account.CardUnlocks.RecordReachedLevel(reachedLevel, accepted?.LeaderCrawlerContentId);
                    foreach (CardUnlockLevelRequirement condition in conditions)
                        if (condition.IsSatisfied(account.CardUnlocks) &&
                            account.TryUnlockCard(condition.CardContentId)) granted.Add(condition.CardContentId);
                    foreach (var condition in behaviors)
                        if (condition.IsSatisfied(account.CardUnlocks.LifetimeFacts, accepted?.Facts) &&
                            account.TryUnlockCard(condition.CardContentId)) granted.Add(condition.CardContentId);
                    persist();
                };
                string eventError;
                bool committed;
                if (accepted == null)
                    committed = account.EventUnlocks.TryCommitExistingFacts(account, commitAndPersist, out eventError);
                else if (accepted.Conclusion == EventUnlockRunConclusion.None)
                    committed = account.EventUnlocks.TryCommitRunFacts(account, accepted.RunId, accepted.Facts, commitAndPersist, out eventError);
                else
                    committed = account.EventUnlocks.TryCommitRunConclusion(account, accepted.RunId, accepted.Facts,
                        accepted.Conclusion, commitAndPersist, out eventError, accepted.EvoProof);
                if (!committed) throw new InvalidOperationException(eventError);
                receipt = new CardUnlockLevelReceipt(beforeProgress.HighestPlayerLevel,
                    account.CardUnlocks.HighestPlayerLevel, granted);
                return true;
            }
            catch (Exception exception)
            {
                if (account != null)
                {
                    account.CardUnlocks.Restore(beforeProgress);
                    account.EventUnlocks.Restore(beforeEvents);
                    if (beforeCards != null)
                    {
                        beforeCards.Clear();
                        beforeCards.AddRange(beforeCardIds);
                    }
                    account.unlockedCards = beforeCards;
                }
                error = "Card level unlock transaction failed: " + exception.Message;
                try { rollbackRun?.Invoke(); }
                catch (Exception rollbackError)
                {
                    error += " Run rollback failed: " + rollbackError.Message;
                }
                return false;
            }
        }
    }
}
