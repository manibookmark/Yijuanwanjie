using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Agents
{
    /// <summary>
    /// Diagnostic intervention on the first published reward offer only. The
    /// requested card must actually be offered; Core still owns generation,
    /// payment and resolution. Later choices use StandardRewardPolicy revision 2.
    /// Create one instance per independent run, preserving it across encounters.
    /// </summary>
    public sealed class FirstCardRewardPolicy : ISimulationRewardPolicy
    {
        public const string IdPrefix = "first-card:";
        private readonly string _cardContentId;
        private readonly StandardRewardPolicy _later = new StandardRewardPolicy();
        private bool _firstSelected;

        public FirstCardRewardPolicy(string cardContentId)
        {
            if (string.IsNullOrWhiteSpace(cardContentId))
                throw new ArgumentException("A stable card contentId is required.", nameof(cardContentId));
            _cardContentId = cardContentId;
        }

        public string Id => IdPrefix + _cardContentId;
        public int Revision => 1;

        public int ChooseReward(CombatController combat,
            IReadOnlyList<LevelUpSystem.Reward> rewards)
        {
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (rewards == null || rewards.Count == 0)
                throw new ArgumentException("Reward offer cannot be empty.", nameof(rewards));
            if (_firstSelected) return _later.ChooseReward(combat, rewards);
            for (int i = 0; i < rewards.Count; i++)
            {
                if (rewards[i]?.type == LevelUpSystem.RewardType.NewCard &&
                    string.Equals(rewards[i].card?.contentId, _cardContentId, StringComparison.Ordinal))
                {
                    _firstSelected = true;
                    return i;
                }
            }
            throw new InvalidOperationException(
                $"First reward offer does not contain {_cardContentId}; diagnostic intervention was not applied.");
        }
    }
}
