namespace WanjieScroll.Core
{
    /// <summary>
    /// Defines the release gate for cards that have entered the manual test registry.
    /// Untracked legacy cards remain available until they are explicitly enrolled.
    /// </summary>
    public static class CardReleasePolicy
    {
        public static bool IsComplete(
            bool uiConfirmed,
            bool functionConfirmed)
        {
            return uiConfirmed && functionConfirmed;
        }

        public static bool ShouldAppearInTest(
            bool isTracked,
            bool uiConfirmed,
            bool functionConfirmed)
        {
            return isTracked && !IsComplete(uiConfirmed, functionConfirmed);
        }

        public static bool CanUseInFormalPlay(
            bool isTracked,
            bool uiConfirmed,
            bool functionConfirmed)
        {
            return !isTracked || IsComplete(uiConfirmed, functionConfirmed);
        }

        /// <summary>New evolution output must have completed manual acceptance; legacy admission does not publish it.</summary>
        public static bool CanUseEvolutionResult(
            bool isTracked,
            bool uiConfirmed,
            bool functionConfirmed)
            => isTracked && IsComplete(uiConfirmed, functionConfirmed);

        /// <summary>
        /// F6 验收战斗既要保留尚未完成的主测卡，也要允许已经完成
        /// 验收（或尚未登记）的卡作为联动支持卡。F7 的卡牌浏览
        /// 另由 Bootstrap 选择策略限定为已登记内容；
        /// <see cref="ShouldAppearInTest"/> 只继续表达“尚未完成验收”。
        /// </summary>
        public static bool CanUseInTestBattle(
            bool isTracked,
            bool uiConfirmed,
            bool functionConfirmed)
        {
            return ShouldAppearInTest(
                    isTracked,
                    uiConfirmed,
                    functionConfirmed) ||
                CanUseInFormalPlay(
                    isTracked,
                    uiConfirmed,
                    functionConfirmed);
        }
    }
}
