namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Run 级只读准入结果。Allowed/ResumePendingChoice 仍须通过楼层自身的
    /// 节点状态校验；这里不替代 LevelRunSession 的开始/完成事务。
    /// </summary>
    public enum DungeonRunNodeAdmission
    {
        Allowed = 0,
        ResumePendingChoice = 1,
        UnknownNode = 2,
        PendingChoiceOutstanding = 3,
        SourcePhaseOutstanding = 4
    }
}
