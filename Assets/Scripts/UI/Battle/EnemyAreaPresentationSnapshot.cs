namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 敌人区域当前静态构成与汇总表现的只读快照。
    /// 它只暴露验证布局所需的值，不泄漏可修改的 Renderer、Transform、
    /// View 或 ScriptableObject 配置实例。
    /// </summary>
    public readonly struct EnemyAreaPresentationSnapshot
    {
        public EnemyAreaPresentationSnapshot(
            string topFogSpriteName,
            float topFogAlpha,
            int topFogSortingOrder,
            float configuredTopFogOpacity,
            int visibleRows,
            bool hasFormationGrid,
            int formationGridLineCount,
            bool hasEnemyStatus,
            int visibleStatusEntryCount,
            float enemyStatusContentWidth)
        {
            TopFogSpriteName = topFogSpriteName;
            TopFogAlpha = topFogAlpha;
            TopFogSortingOrder = topFogSortingOrder;
            ConfiguredTopFogOpacity = configuredTopFogOpacity;
            VisibleRows = visibleRows;
            HasFormationGrid = hasFormationGrid;
            FormationGridLineCount = formationGridLineCount;
            HasEnemyStatus = hasEnemyStatus;
            VisibleStatusEntryCount = visibleStatusEntryCount;
            EnemyStatusContentWidth = enemyStatusContentWidth;
        }

        public string TopFogSpriteName { get; }
        public float TopFogAlpha { get; }
        public int TopFogSortingOrder { get; }
        public float ConfiguredTopFogOpacity { get; }
        public int VisibleRows { get; }
        public bool HasFormationGrid { get; }
        public int FormationGridLineCount { get; }
        public bool HasEnemyStatus { get; }
        public int VisibleStatusEntryCount { get; }
        public float EnemyStatusContentWidth { get; }
    }
}
