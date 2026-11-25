namespace WanjieScroll.Dungeon
{
    public enum ExplorationEventType
    {
        RoomEntered,
        EncounterStarted,
        BossEncountered,
        TreasureOpened,
        PlayerHealed,
        MerchantEntered,
        LightBroken,
        StateChanged
    }

    public sealed class ExplorationEvent
    {
        public ExplorationEvent(ExplorationEventType type, Room room, int amount = 0)
        {
            Type = type;
            Room = room;
            Amount = amount;
        }

        public ExplorationEventType Type { get; }
        public Room Room { get; }
        public int Amount { get; }
    }
}
