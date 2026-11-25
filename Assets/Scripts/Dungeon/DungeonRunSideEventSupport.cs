using System;
using System.Collections.Generic;

namespace WanjieScroll.Dungeon
{
    /// <summary>Preflights every referenced occurrence through the actual execution adapter,
    /// without eligibility, RNG or allocation. Closed eligibility gates cannot hide
    /// missing mechanics inside the approved scope. Candidate diagnostics explicitly
    /// inspect the full source; PartialEnabled execution retains future declarations.</summary>
    public static class DungeonRunSideEventSupport
    {
        public static IReadOnlyList<string> Inspect(DungeonRunSideEventPlan plan, bool includeUnapproved = false)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var errors = new List<string>();
            var source = plan.Document.Definition;
            foreach (var layer in source.Layers)
            {
                if (!includeUnapproved && plan.ApprovedSourceLayerCount > 0 &&
                    !plan.IsSourceLayerApproved(layer.ContentId)) continue;
                foreach (var slot in layer.Slots)
                {
                    var members = slot.InlineMembers;
                    if (slot.Mode == Core.SideEventSourceSlotMode.SharedGroup)
                    { source.TryGetGroup(slot.GroupContentId, out var group); members = group.Members; }
                    foreach (var member in members)
                        if (!DungeonSideEventSourceAdapter.TryResolveMemberAdapterIdentity(source, member,
                                out _, out string error))
                            errors.Add(layer.ContentId + "/" + slot.ContentId + "/" + member.RootContentId + ": " + error);
                }
            }
            return errors.AsReadOnly();
        }
    }
}
