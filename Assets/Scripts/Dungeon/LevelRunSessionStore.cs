namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 旧楼层会话入口的兼容门面。权威状态由
    /// <see cref="DungeonRunSessionStore"/> 持有。
    /// </summary>
    public static class LevelRunSessionStore
    {
        public static LevelRunSession Current =>
            DungeonRunSessionStore.Current?.CurrentFloor;

        public static LevelRunSession Begin(
            LevelRunProfile profile,
            string snapshotId,
            string runId = null)
        {
            return DungeonRunSessionStore.Begin(
                profile, snapshotId, runId).CurrentFloor;
        }

        public static LevelRunSession BeginConfigured(
            string levelContentId,
            string snapshotId = null,
            string runId = null)
        {
            return DungeonRunSessionStore.BeginConfigured(
                levelContentId, snapshotId, runId).CurrentFloor;
        }

        public static void Clear()
        {
            DungeonRunSessionStore.Clear();
        }
    }
}
