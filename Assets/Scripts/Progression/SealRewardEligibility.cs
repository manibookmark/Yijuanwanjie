using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Owns the permanent permission to receive and immediately assign seals.
    /// Seal-kind unlocks describe candidates; they never grant this capability.
    /// Reads the current account without caching or changing progression.
    /// </summary>
    public static class SealRewardEligibility
    {
        public static bool CanReceiveSeals(MetaProgression account)
            => account != null && account.IsRelicUnlocked(RelicContentIds.Yuyin) &&
               account.HasRelicType(RelicType.GemHammer);
    }
}
