using System;

namespace WanjieScroll.Core
{
    /// <summary>The original source survives rerolls; reroll is a request operation.</summary>
    public enum LevelUpRewardSource
    {
        CombatLevelUp,
        MapExperience,
        MapReferenceLevel
    }

    /// <summary>
    /// Immutable context for one reached-level reward. It owns no pending offer,
    /// qualification, random state or charge. Callers persist it with frozen choices.
    /// </summary>
    public readonly struct LevelUpRewardRequest
    {
        public int ReachedLevel { get; }
        public LevelUpRewardSource Source { get; }
        public string RuleContentId { get; }
        public int RuleVersion { get; }
        public bool IsReroll { get; }

        public LevelUpRewardRequest(int reachedLevel,
            LevelUpRewardSource source = LevelUpRewardSource.CombatLevelUp,
            string ruleContentId = null, int ruleVersion = 0, bool isReroll = false)
        {
            if (reachedLevel < 0) throw new ArgumentOutOfRangeException(nameof(reachedLevel));
            if (!Enum.IsDefined(typeof(LevelUpRewardSource), source))
                throw new ArgumentOutOfRangeException(nameof(source));
            if (ruleVersion < 0) throw new ArgumentOutOfRangeException(nameof(ruleVersion));
            ReachedLevel = reachedLevel;
            Source = source;
            RuleContentId = ruleContentId;
            RuleVersion = ruleVersion;
            IsReroll = isReroll;
        }

        public LevelUpRewardRequest AsReroll() => new LevelUpRewardRequest(
            ReachedLevel, Source, RuleContentId, RuleVersion, true);
    }
}
