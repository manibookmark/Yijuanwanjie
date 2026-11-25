using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>An immutable authored choice. Reference identity is the consecutive-choice identity.</summary>
    public sealed class EnemyHealthBehaviorChoice
    {
        public EnemyHealthBehaviorChoice(
            EnemyBehaviorAction action,
            float weight = 1f,
            bool canBeConsecutive = true,
            bool prioritiseForStartPhase = false)
        {
            if (action.IntentType == EnemyIntentType.None ||
                string.IsNullOrWhiteSpace(action.Description))
                throw new ArgumentException("A health-group choice requires a configured action.", nameof(action));
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f)
                throw new ArgumentOutOfRangeException(nameof(weight), "Choice weight must be finite and nonnegative.");
            Action = action;
            Weight = weight;
            CanBeConsecutive = canBeConsecutive;
            PrioritiseForStartPhase = prioritiseForStartPhase;
        }

        public EnemyBehaviorAction Action { get; }
        public float Weight { get; }
        public bool CanBeConsecutive { get; }
        public bool PrioritiseForStartPhase { get; }
    }

    public sealed class EnemyHealthBehaviorGroup
    {
        public EnemyHealthBehaviorGroup(
            float requiredHealthPercent,
            IReadOnlyList<EnemyHealthBehaviorChoice> choices,
            int turnCountdown = -1)
        {
            if (float.IsNaN(requiredHealthPercent) || float.IsInfinity(requiredHealthPercent) ||
                requiredHealthPercent < 0f || requiredHealthPercent > 100f)
                throw new ArgumentOutOfRangeException(nameof(requiredHealthPercent));
            if (choices == null)
                throw new ArgumentNullException(nameof(choices));
            var copy = new EnemyHealthBehaviorChoice[choices.Count];
            float total = 0f;
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = choices[i] ?? throw new ArgumentException("Choices cannot contain null.", nameof(choices));
                total = (float)((double)total + copy[i].Weight);
                if (float.IsInfinity(total))
                    throw new ArgumentException("The float weight sum must be finite.", nameof(choices));
            }
            RequiredHealthPercent = requiredHealthPercent;
            Choices = Array.AsReadOnly(copy);
            TurnCountdown = turnCountdown;
        }

        public float RequiredHealthPercent { get; }
        public IReadOnlyList<EnemyHealthBehaviorChoice> Choices { get; }
        /// <summary>Authored signed countdown. Only positive values admit an interrupt group.</summary>
        public int TurnCountdown { get; }
        public bool TriggersInterrupt => TurnCountdown > 0;
    }

    /// <summary>
    /// Owns immutable, ordered health groups and their temporary-card invariants.
    /// It never sorts groups or flattens choice identity. Binding copies the graph,
    /// preserving a shared authored choice as one shared compiled choice across groups.
    /// Runtime selection state belongs exclusively to EnemyHealthBehaviorSelector.
    /// </summary>
    public sealed class EnemyHealthBehaviorDefinition
    {
        public EnemyHealthBehaviorDefinition(
            string contentId,
            IReadOnlyList<EnemyHealthBehaviorGroup> groups)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("A stable behavior contentId is required.", nameof(contentId));
            if (groups == null)
                throw new ArgumentNullException(nameof(groups));
            ContentId = contentId.Trim();
            var compiled = new Dictionary<EnemyHealthBehaviorChoice, EnemyHealthBehaviorChoice>();
            var limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var result = new EnemyHealthBehaviorGroup[groups.Count];
            for (int g = 0; g < result.Length; g++)
            {
                EnemyHealthBehaviorGroup group = groups[g] ??
                    throw new ArgumentException("Groups cannot contain null.", nameof(groups));
                var choices = new EnemyHealthBehaviorChoice[group.Choices.Count];
                for (int c = 0; c < choices.Length; c++)
                {
                    EnemyHealthBehaviorChoice original = group.Choices[c];
                    if (!compiled.TryGetValue(original, out EnemyHealthBehaviorChoice choice))
                    {
                        ValidateTemporaryCard(original.Action.TemporaryCard, limits);
                        if (original.Action.IntentType == EnemyIntentType.Compound)
                            ValidateTemporaryCard(original.Action.Compound.SecondaryTemporaryCard, limits);
                        choice = CopyChoice(original,
                            original.Action.WithTemporaryCardSourceBehavior(ContentId));
                        compiled.Add(original, choice);
                    }
                    choices[c] = choice;
                }
                result[g] = new EnemyHealthBehaviorGroup(group.RequiredHealthPercent, choices, group.TurnCountdown);
            }
            Groups = Array.AsReadOnly(result);
        }

        public string ContentId { get; }
        public IReadOnlyList<EnemyHealthBehaviorGroup> Groups { get; }

        /// <summary>
        /// Pure native-order health query. It neither selects a choice nor consumes
        /// a phase transition; interrupt admission can query without losing priority.
        /// </summary>
        public int ResolveHealthGroupIndex(int currentHealth, int maxHealth)
        {
            if (currentHealth < 0) throw new ArgumentOutOfRangeException(nameof(currentHealth));
            if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            float ratio = (float)((double)currentHealth / maxHealth);
            int phase = -1;
            for (int i = 0; i < Groups.Count; i++)
            {
                float threshold = (float)((double)Groups[i].RequiredHealthPercent / 100d);
                if (threshold < ratio) break;
                phase = i;
            }
            return phase;
        }

        public IEnumerable<EnemyBehaviorAction> EnumerateActions()
        {
            foreach (EnemyHealthBehaviorGroup group in Groups)
                foreach (EnemyHealthBehaviorChoice choice in group.Choices)
                    yield return choice.Action;
        }

        public EnemyHealthBehaviorDefinition BindTemporaryCardDefinitions(Func<string, CardConfig> resolver)
        {
            bool needsBinding = false;
            foreach (EnemyBehaviorAction action in EnumerateActions())
                if (GetTemporaryCard(action).IsConfigured)
                {
                    needsBinding = true;
                    break;
                }
            if (!needsBinding)
                return this;
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            var bound = new Dictionary<EnemyHealthBehaviorChoice, EnemyHealthBehaviorChoice>();
            var groups = new EnemyHealthBehaviorGroup[Groups.Count];
            for (int g = 0; g < groups.Length; g++)
            {
                EnemyHealthBehaviorGroup group = Groups[g];
                var choices = new EnemyHealthBehaviorChoice[group.Choices.Count];
                for (int c = 0; c < choices.Length; c++)
                {
                    EnemyHealthBehaviorChoice original = group.Choices[c];
                    if (!bound.TryGetValue(original, out EnemyHealthBehaviorChoice choice))
                    {
                        EnemyBehaviorAction action = original.Action;
                        EnemyTemporaryCardRequest temporary = GetTemporaryCard(action);
                        if (temporary.IsConfigured)
                        {
                            CardConfig card = resolver(temporary.ContentId);
                            if (card == null)
                                throw new InvalidOperationException(
                                    $"Behavior {ContentId} temporary card does not exist: {temporary.ContentId}.");
                            action = action.WithTemporaryCardDefinition(card);
                        }
                        choice = CopyChoice(original, action);
                        bound.Add(original, choice);
                    }
                    choices[c] = choice;
                }
                groups[g] = new EnemyHealthBehaviorGroup(group.RequiredHealthPercent, choices, group.TurnCountdown);
            }
            return new EnemyHealthBehaviorDefinition(ContentId, groups);
        }

        private void ValidateTemporaryCard(EnemyTemporaryCardRequest temporary, Dictionary<string, int> limits)
        {
            if (!temporary.IsConfigured)
                return;
            if (!string.IsNullOrEmpty(temporary.SourceBehaviorContentId) &&
                !string.Equals(temporary.SourceBehaviorContentId, ContentId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Temporary-card source behavior must match its owning definition.");
            if (limits.TryGetValue(temporary.ContentId, out int existing) && existing != temporary.MaxRemaining)
                throw new ArgumentException("The same temporary-card ID must have one MaxRemaining across all groups.");
            limits[temporary.ContentId] = temporary.MaxRemaining;
        }

        private static EnemyTemporaryCardRequest GetTemporaryCard(EnemyBehaviorAction action)
            => action.IntentType == EnemyIntentType.Compound
                ? action.Compound.SecondaryTemporaryCard
                : action.TemporaryCard;

        private static EnemyHealthBehaviorChoice CopyChoice(EnemyHealthBehaviorChoice choice, EnemyBehaviorAction action)
            => new EnemyHealthBehaviorChoice(action, choice.Weight,
                choice.CanBeConsecutive, choice.PrioritiseForStartPhase);
    }
}
