namespace WanjieScroll.Core
{
    public enum CombatRuleTriggerKind { ExperimentalMedicine, ShieldBash }

    /// <summary>A resolved rule contribution, independent of UI and account configuration.</summary>
    public readonly struct CombatRuleTriggeredEvent
    {
        public CombatRuleTriggerKind Kind { get; }
        public int Amount { get; }
        public CombatRuleTriggeredEvent(CombatRuleTriggerKind kind, int amount)
        {
            Kind = kind;
            Amount = amount;
        }
    }
}
