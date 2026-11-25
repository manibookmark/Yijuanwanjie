using WanjieScroll.Core;

namespace WanjieScroll.Bootstrap
{
    public enum VfxTestCardSelectionFilter
    {
        Pending,
        All,
        Confirmed
    }

    /// <summary>
    /// Owns F7 card-list eligibility. Unregistered content never enters the
    /// selector; runtime and Inspector filters only narrow that registered
    /// set. Pending means either manual confirmation is still missing, and
    /// filtering never changes formal release state.
    /// </summary>
    public static class VfxTestCardSelectionPolicy
    {
        public static bool ShouldInclude(
            string contentId,
            bool hideConfirmed)
        {
            return ShouldInclude(contentId, hideConfirmed
                ? VfxTestCardSelectionFilter.Pending
                : VfxTestCardSelectionFilter.All);
        }

        public static bool ShouldInclude(
            string contentId,
            VfxTestCardSelectionFilter filter)
        {
            return ShouldInclude(
                CardTestStatus.IsTracked(contentId),
                CardTestStatus.IsReady(contentId),
                filter);
        }

        public static bool ShouldInclude(
            bool isTracked,
            bool isConfirmed,
            bool hideConfirmed)
        {
            return ShouldInclude(isTracked, isConfirmed, hideConfirmed
                ? VfxTestCardSelectionFilter.Pending
                : VfxTestCardSelectionFilter.All);
        }

        public static bool ShouldInclude(
            bool isTracked,
            bool isConfirmed,
            VfxTestCardSelectionFilter filter)
        {
            if (!isTracked) return false;
            switch (filter)
            {
                case VfxTestCardSelectionFilter.Pending: return !isConfirmed;
                case VfxTestCardSelectionFilter.All: return true;
                case VfxTestCardSelectionFilter.Confirmed: return isConfirmed;
                default: return false;
            }
        }
    }
}
