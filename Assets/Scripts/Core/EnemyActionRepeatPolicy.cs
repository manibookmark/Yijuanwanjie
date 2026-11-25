using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Immutable selection constraint. Stable action identity crosses phases;
    /// anonymous legacy actions can only match an index in the same phase.
    /// StageMode also tells health selectors to use successful execution history
    /// rather than the native last-selected-object consecutive rule.
    /// </summary>
    internal readonly struct EnemyActionRepeatConstraint
    {
        internal EnemyActionRepeatConstraint(bool stageMode, string actionId, int phaseIndex, int choiceIndex)
        {
            StageMode = stageMode;
            _actionId = actionId;
            _phaseIndex = phaseIndex;
            _choiceIndex = choiceIndex;
            _hasHistory = !string.IsNullOrWhiteSpace(actionId) || choiceIndex >= 0;
        }

        private readonly string _actionId;
        private readonly int _phaseIndex;
        private readonly int _choiceIndex;
        private readonly bool _hasHistory;
        internal bool StageMode { get; }

        internal bool Excludes(string actionId, int choiceIndex, int phaseIndex)
        {
            if (!_hasHistory) return false;
            return !string.IsNullOrWhiteSpace(_actionId)
                ? string.Equals(_actionId, actionId, StringComparison.Ordinal)
                : phaseIndex == _phaseIndex && choiceIndex == _choiceIndex;
        }
    }

    /// <summary>
    /// One encounter's successful action history and automatic Stage policy.
    /// Only the AI's successful receipt boundary calls Commit. Selection,
    /// cancellation and failed claims cannot write history. Definition and actor
    /// references prevent reused IDs or replacement definitions borrowing memory.
    /// Existing selectors retain phase, qualification and probability ownership;
    /// their single draw consumes this immutable exclusion, with a no-alternative
    /// fallback. No configuration cloning or per-level knowledge belongs here.
    /// </summary>
    internal sealed class EnemyActionRepeatPolicy
    {
        internal const string FallbackAttackId = "action.core.stage.fallback.attack";
        internal const string FallbackDefendId = "action.core.stage.fallback.defend";

        private readonly struct ResolvedChoice
        {
            internal ResolvedChoice(EnemyActionReceipt receipt)
            {
                Actor = receipt.Actor;
                Definition = receipt.Definition;
                ActionId = receipt.Intent.ActionId;
                PhaseIndex = receipt.PhaseIndex;
                ChoiceIndex = receipt.ChoiceIndex;
            }
            internal EnemyStats Actor { get; }
            internal EnemyBehaviorDefinition Definition { get; }
            internal string ActionId { get; }
            internal int PhaseIndex { get; }
            internal int ChoiceIndex { get; }
        }

        private readonly Dictionary<int, ResolvedChoice> _resolved = new Dictionary<int, ResolvedChoice>();

        internal EnemyActionRepeatConstraint Capture(EnemyStats enemy)
        {
            bool stage = enemy?.bossRank == BossRank.Stage;
            if (!Applies(enemy)) return default;
            if (_resolved.TryGetValue(enemy.runtimeId, out ResolvedChoice previous) &&
                ReferenceEquals(previous.Actor, enemy) &&
                ReferenceEquals(previous.Definition, enemy.behaviorDefinition))
                return new EnemyActionRepeatConstraint(stage, previous.ActionId,
                    previous.PhaseIndex, previous.ChoiceIndex);
            return new EnemyActionRepeatConstraint(stage, null, -1, -1);
        }

        internal void Commit(EnemyActionReceipt receipt)
        {
            if (receipt == null || receipt.Intent.IsNone || !Applies(receipt.Actor)) return;
            _resolved[receipt.EnemyRuntimeId] = new ResolvedChoice(receipt);
        }

        private static bool Applies(EnemyStats enemy)
            => enemy != null && (enemy.bossRank == BossRank.Stage ||
                enemy.behaviorDefinition != null && enemy.behaviorDefinition.HealthGroups == null &&
                enemy.behaviorDefinition.SelectionMode == EnemyBehaviorSelectionMode.WeightedRandom &&
                enemy.behaviorDefinition.AvoidImmediateRepeat);

        /// <summary>Stage's unconfigured 70/20/10 default, sampled once. None
        /// stays None, emits no receipt and therefore cannot replace history.</summary>
        internal EnemyIntent SelectStageFallback(EnemyStats enemy, int attackDamage,
            Func<float> randomValue, out int choiceIndex)
        {
            EnemyActionRepeatConstraint constraint = Capture(enemy);
            bool excludeAttack = constraint.Excludes(FallbackAttackId, 0, 0);
            bool excludeDefend = constraint.Excludes(FallbackDefendId, 1, 0);
            float total = (excludeAttack ? 0f : 70f) + (excludeDefend ? 0f : 20f) + 10f;
            float roll = randomValue() * total;
            if (!excludeAttack)
            {
                if (roll < 70f)
                {
                    choiceIndex = 0;
                    return EnemyIntent.Attack(attackDamage, actionId: FallbackAttackId);
                }
                roll -= 70f;
            }
            if (!excludeDefend && roll < 20f)
            {
                choiceIndex = 1;
                return EnemyIntent.Defend(5, actionId: FallbackDefendId);
            }
            choiceIndex = -1;
            return EnemyIntent.None;
        }
    }
}
