using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public readonly struct EnemyHealthBehaviorSnapshot
    {
        internal EnemyHealthBehaviorSnapshot(int phase, int choicePhase, int choiceIndex)
        {
            CurrentPhaseIndex = phase;
            ChoicePhaseIndex = choicePhase;
            ChoiceIndex = choiceIndex;
        }

        public int CurrentPhaseIndex { get; }
        public int ChoicePhaseIndex { get; }
        public int ChoiceIndex { get; }
        public bool HasChoice => ChoiceIndex >= 0;
    }

    public readonly struct EnemyHealthBehaviorPlan
    {
        internal EnemyHealthBehaviorPlan(EnemyHealthBehaviorSnapshot snapshot, EnemyHealthBehaviorChoice choice)
        {
            HasChoice = choice != null;
            Action = choice == null ? default : choice.Action;
            CurrentPhaseIndex = snapshot.CurrentPhaseIndex;
            ChoicePhaseIndex = snapshot.ChoicePhaseIndex;
            ChoiceIndex = snapshot.ChoiceIndex;
        }

        public bool HasChoice { get; }
        public EnemyBehaviorAction Action { get; }
        public int CurrentPhaseIndex { get; }
        public int ChoicePhaseIndex { get; }
        public int ChoiceIndex { get; }
    }

    /// <summary>
    /// The single owner of one enemy's health phase and currently selected choice.
    /// Plan selects immediately; observing Snapshot never draws or advances state.
    /// This adopts the audited native health-selection algorithm, including empty
    /// pool clearing and weighted exhaustion retaining an earlier selected choice.
    /// Interrupt countdown, death/freeze gating and execution remain separate owners.
    /// </summary>
    public sealed class EnemyHealthBehaviorSelector
    {
        private int _currentPhaseIndex = -1;
        private int _choicePhaseIndex = -1;
        private int _choiceIndex = -1;
        private EnemyHealthBehaviorChoice _currentChoice;
        private bool _isPlanning;

        public EnemyHealthBehaviorSelector(EnemyHealthBehaviorDefinition definition, int currentHealth, int maxHealth)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            // Native SetConfig consumes the initial phase change before normal selection.
            Refresh(currentHealth, maxHealth);
        }

        public EnemyHealthBehaviorDefinition Definition { get; }
        public EnemyHealthBehaviorSnapshot Snapshot
            => new EnemyHealthBehaviorSnapshot(_currentPhaseIndex, _choicePhaseIndex, _choiceIndex);

        /// <summary>Explicit intent cancellation releases consecutive memory without changing the phase or drawing.</summary>
        public void ClearCurrentChoice()
        {
            if (_isPlanning)
                throw new InvalidOperationException("A health behavior selection is already being planned.");
            ClearChoice();
        }

        public EnemyHealthBehaviorPlan Plan(
            int currentHealth,
            int maxHealth,
            Func<float, float, float> randomRange,
            Func<EnemyBehaviorAction, bool> canExecute)
            => Plan(currentHealth, maxHealth, randomRange, canExecute, default);

        /// <summary>Only Stage actors receive a resolved-history policy. Other
        /// callers retain the audited native consecutive and exhaustion behavior.</summary>
        internal EnemyHealthBehaviorPlan Plan(int currentHealth, int maxHealth,
            Func<float, float, float> randomRange, Func<EnemyBehaviorAction, bool> canExecute,
            EnemyActionRepeatConstraint repeat)
        {
            if (_isPlanning)
                throw new InvalidOperationException("A health behavior selection is already being planned.");
            if (randomRange == null)
                throw new ArgumentNullException(nameof(randomRange));
            if (canExecute == null)
                throw new ArgumentNullException(nameof(canExecute));
            _isPlanning = true;
            try
            {
                bool changed = Refresh(currentHealth, maxHealth);
                if (_currentPhaseIndex < 0)
                    return ClearChoice();
                IReadOnlyList<EnemyHealthBehaviorChoice> choices = Definition.Groups[_currentPhaseIndex].Choices;
                bool anyPriority = false;
                for (int i = 0; i < choices.Count; i++)
                    anyPriority |= choices[i].PrioritiseForStartPhase;

                var legal = new List<int>();
                float total = 0f;
                for (int i = 0; i < choices.Count; i++)
                {
                    EnemyHealthBehaviorChoice choice = choices[i];
                    if (!repeat.StageMode && ReferenceEquals(choice, _currentChoice) && !choice.CanBeConsecutive)
                        continue;
                    if (changed && anyPriority && !choice.PrioritiseForStartPhase)
                        continue;
                    if (!canExecute(choice.Action))
                        continue;
                    legal.Add(i);
                    total = (float)((double)total + choice.Weight);
                }
                if (legal.Count == 0)
                    return ClearChoice();

                // Qualification and phase priority remain authoritative. Stage's
                // shared rule only removes the previous successful action when a
                // different legal choice exists; it never retries the random draw.
                if (repeat.StageMode)
                {
                    // Zero weight is not a probabilistic alternative. An all-zero
                    // Stage pool has no selectable action, rather than retaining a
                    // previous choice or promoting a zero-probability move to 100%.
                    legal.RemoveAll(index => choices[index].Weight <= 0f);
                    if (legal.Count == 0) return ClearChoice();
                    bool hasAlternative = false;
                    foreach (int index in legal)
                        if (!repeat.Excludes(choices[index].Action.ActionId, index, _currentPhaseIndex))
                            hasAlternative = true;
                    if (hasAlternative)
                        legal.RemoveAll(index => repeat.Excludes(
                            choices[index].Action.ActionId, index, _currentPhaseIndex));
                    total = 0f;
                    foreach (int index in legal)
                        total = (float)((double)total + choices[index].Weight);
                }

                float draw = randomRange(0f, total);
                if (float.IsNaN(draw) || float.IsInfinity(draw) || draw < 0f || draw > total)
                    throw new ArgumentOutOfRangeException(nameof(randomRange), "The draw must be finite and within the inclusive float range.");
                float cumulative = 0f;
                int lastLegal = -1;
                bool selected = false;
                foreach (int index in legal)
                {
                    EnemyHealthBehaviorChoice choice = choices[index];
                    // The native traversal checks action qualification again without rebuilding total.
                    if (!canExecute(choice.Action))
                        continue;
                    lastLegal = index;
                    cumulative = (float)((double)cumulative + choice.Weight);
                    if (cumulative > draw)
                    {
                        _currentChoice = choice;
                        _choicePhaseIndex = _currentPhaseIndex;
                        _choiceIndex = index;
                        selected = true;
                        break;
                    }
                }
                if (repeat.StageMode && !selected)
                {
                    // Native float's inclusive upper endpoint
                    // must not resurrect an excluded old Stage action. Use a
                    // currently qualified remaining candidate, or a true empty pool.
                    if (lastLegal < 0) return ClearChoice();
                    _currentChoice = choices[lastLegal];
                    _choicePhaseIndex = _currentPhaseIndex;
                    _choiceIndex = lastLegal;
                }
                // Non-Stage deliberately keeps native exhaustion's old choice.
                return new EnemyHealthBehaviorPlan(Snapshot, _currentChoice);
            }
            finally
            {
                _isPlanning = false;
            }
        }

        private EnemyHealthBehaviorPlan ClearChoice()
        {
            _currentChoice = null;
            _choicePhaseIndex = -1;
            _choiceIndex = -1;
            return new EnemyHealthBehaviorPlan(Snapshot, null);
        }

        private bool Refresh(int currentHealth, int maxHealth)
        {
            int phase = Definition.ResolveHealthGroupIndex(currentHealth, maxHealth);
            bool changed = phase != _currentPhaseIndex;
            _currentPhaseIndex = phase;
            return changed;
        }

    }
}
