namespace WanjieScroll.Core
{
    /// <summary>
    /// An immutable identity for one enemy action batch entry. Selection and later
    /// previews never rewrite it; only its issuing AI owns consumption/cancellation.
    /// It carries no queue, actor state or public mutable completion flag.
    /// </summary>
    public sealed class EnemyActionReceipt
    {
        internal EnemyActionReceipt(int enemyIndex, EnemyStats actor,
            EnemyBehaviorDefinition definition, int choiceIndex, int phaseIndex,
            int actionIndex, EnemyIntent intent)
        {
            EnemyIndex = enemyIndex;
            Actor = actor;
            EnemyRuntimeId = actor.runtimeId;
            Definition = definition;
            ChoiceIndex = choiceIndex;
            PhaseIndex = phaseIndex;
            ActionIndex = actionIndex;
            Intent = intent;
        }

        public int EnemyIndex { get; }
        public int EnemyRuntimeId { get; }
        public string BehaviorContentId => Definition?.ContentId;
        public int ChoiceIndex { get; }
        public int PhaseIndex { get; }
        public EnemyIntent Intent { get; }
        internal EnemyStats Actor { get; }
        internal EnemyBehaviorDefinition Definition { get; }
        internal int ActionIndex { get; }
    }
}
