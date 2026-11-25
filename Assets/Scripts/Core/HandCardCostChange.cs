namespace WanjieScroll.Core
{
    /// <summary>
    /// One committed numeric hand-cost write, captured by the deck owner.
    /// Costs are the card's effective numeric values (including negatives),
    /// independent of one-use free payment. Repeated writes stay in order.
    /// </summary>
    public readonly struct HandCardCostChange
    {
        public int RuntimeInstanceId { get; }
        public int BeforeCost { get; }
        public int AfterCost { get; }

        public HandCardCostChange(int runtimeInstanceId, int beforeCost, int afterCost)
        {
            RuntimeInstanceId = runtimeInstanceId;
            BeforeCost = beforeCost;
            AfterCost = afterCost;
        }
    }
}
