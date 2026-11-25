namespace WanjieScroll.Meta
{
    /// <summary>
    /// 教程是否完成的唯一判定入口。旧存档可能没有关卡通关 ID，
    /// 但已经拥有教程终局发放的孙悟空，因此将其视为已完成。
    /// </summary>
    public static class TutorialProgressionGate
    {
        public const string LevelContentId =
            "level.tutorial.huaguoshan.v1";
        public const string WukongCrawlerContentId =
            "legacy.crawler.common.5a3f3db3";

        public static bool IsCompleted(MetaProgression progression)
        {
            return progression != null &&
                   (progression.IsLevelCleared(LevelContentId) ||
                    progression.IsCrawlerUnlocked(
                        WukongCrawlerContentId));
        }
    }
}
