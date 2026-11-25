using System;

namespace WanjieScroll.Dungeon
{
    /// <summary>One immutable admission dependency pair; no second Run or source journal.</summary>
    public sealed class DungeonRunSideEventSetup
    {
        public DungeonRunSideEventPlan Plan { get; }
        public IDungeonRunSideEventContext Context { get; }
        public DungeonRunSideEventSetup(DungeonRunSideEventPlan plan, IDungeonRunSideEventContext context)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }
    }
}
