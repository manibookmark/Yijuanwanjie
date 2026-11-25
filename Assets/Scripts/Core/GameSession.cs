namespace WanjieScroll.Core
{
    /// <summary>
    /// 跨场景持久状态对象。持有玩家在场景间传递的运行时数据。
    /// <para>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>普通 C# 类（非 MonoBehaviour），可注入、可测试。</item>
    /// <item>字段名和语义与 <c>GameState</c> 静态类完全对齐，方便迁移。</item>
    /// <item><see cref="Current"/> 静态属性作为过渡期访问点，等全部场景迁移到 Bootstrap 注入后可移除。</item>
    /// <item><c>GameState</c> 不删除、不修改，新旧并行。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class GameSession
    {
        public const string DefaultCrawlerContentId =
            "legacy.crawler.common.5a3f3db3";

        /// <summary>本局地牢中跨战斗保留的天运、敛玉与灵玉收益。</summary>
        public RunProgressionState RunProgression { get; private set; } = new RunProgressionState();

        /// <summary>
        /// The active Run host binds its already-prepared progression by reference.
        /// This is an alias for legacy scene consumers, never a second state copy.
        /// Call only after the host has validated a complete replacement Run.
        /// </summary>
        public void BindRunProgression(RunProgressionState progression)
        {
            RunProgression = progression ?? throw new System.ArgumentNullException(nameof(progression));
        }

        /// <summary>当前选中的卷（0=神话, 1=历史, 2=修仙）</summary>
        public int SelectedVolumeId { get; set; } = 0;

        /// <summary>当前选中的地牢索引</summary>
        public int SelectedDungeonIndex { get; set; } = 0;

        /// <summary>玩家选择的英灵 ID</summary>
        public string SelectedCrawlerId { get; set; } =
            DefaultCrawlerContentId;

        /// <summary>玩家灵玉（局外货币）；新账号从零开始，余额由实际奖励或存档恢复。</summary>
        public int PlayerLingyu { get; set; } = 0;

        /// <summary>已通关的关卡数</summary>
        public int ClearedLevels { get; set; } = 0;

        /// <summary>开始新局时清空所有仅限本局的增量；永久灵玉不清空。</summary>
        public void StartNewRun()
        {
            RunProgression.ResetRun();
        }

        /// <summary>获取当前卷的 ContentDatabase ID</summary>
        public string GetVolumeId()
        {
            return SelectedVolumeId switch
            {
                0 => "xiyou",
                1 => "daming",
                2 => "xiuxian",
                _ => "xiyou"
            };
        }

        /// <summary>获取当前卷的中文名</summary>
        public string GetVolumeName()
        {
            return SelectedVolumeId switch
            {
                0 => "神话卷",
                1 => "历史卷",
                2 => "修仙卷",
                _ => "神话卷"
            };
        }

        // ──────────────────────────────────────────────
        //  静态访问点（过渡期使用）
        // ──────────────────────────────────────────────

        private static GameSession _current;

        /// <summary>
        /// 当前全局 GameSession 实例。
        /// 首次访问时自动创建。等全部场景迁移到 Bootstrap 注入后可移除此静态属性。
        /// </summary>
        public static GameSession Current
        {
            get => _current ??= new GameSession();
            set => _current = value;
        }
    }
}
