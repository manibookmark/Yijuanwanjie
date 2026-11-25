namespace WanjieScroll.UI.Hub
{
    /// <summary>Immutable transport for the portal's state animation values.</summary>
    public readonly struct HubPortalMotionSettings
    {
        public HubPortalMotionSettings(
            float idleBaseFlowStrength,
            float idleFlowInterval,
            float idleFlowDuration,
            float idleFlowStrength,
            float idleFlowSpeed,
            float hoverSpinSpeedDegrees,
            float hoverReturnSpeedDegrees,
            float openingSpinDuration,
            float openingSpinTurns,
            float openingSinkStrength)
        {
            IdleBaseFlowStrength = idleBaseFlowStrength;
            IdleFlowInterval = idleFlowInterval;
            IdleFlowDuration = idleFlowDuration;
            IdleFlowStrength = idleFlowStrength;
            IdleFlowSpeed = idleFlowSpeed;
            HoverSpinSpeedDegrees = hoverSpinSpeedDegrees;
            HoverReturnSpeedDegrees = hoverReturnSpeedDegrees;
            OpeningSpinDuration = openingSpinDuration;
            OpeningSpinTurns = openingSpinTurns;
            OpeningSinkStrength = openingSinkStrength;
        }

        public float IdleBaseFlowStrength { get; }
        public float IdleFlowInterval { get; }
        public float IdleFlowDuration { get; }
        public float IdleFlowStrength { get; }
        public float IdleFlowSpeed { get; }
        public float HoverSpinSpeedDegrees { get; }
        public float HoverReturnSpeedDegrees { get; }
        public float OpeningSpinDuration { get; }
        public float OpeningSpinTurns { get; }
        public float OpeningSinkStrength { get; }
    }
}
