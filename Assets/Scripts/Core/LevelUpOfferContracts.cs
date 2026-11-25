using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// A resolved, deterministic level-up offer.  Content and Bootstrap own
    /// the stable-ID lookup; Core only receives executable reward objects and
    /// keeps the selection lifecycle authoritative.
    /// </summary>
    public sealed class LevelUpRewardOffer
    {
        private readonly List<LevelUpSystem.Reward> _rewards;

        public string ContentId { get; }
        public IReadOnlyList<LevelUpSystem.Reward> Rewards => _rewards;
        public bool AllowsCashOut { get; }

        public LevelUpRewardOffer(
            string contentId,
            IReadOnlyList<LevelUpSystem.Reward> rewards,
            bool allowsCashOut)
        {
            if (string.IsNullOrWhiteSpace(contentId))
            {
                throw new ArgumentException(
                    "固定升级 Offer 必须使用稳定 contentId。",
                    nameof(contentId));
            }
            if (rewards == null || rewards.Count == 0)
            {
                throw new ArgumentException(
                    "固定升级 Offer 至少需要一个候选。",
                    nameof(rewards));
            }

            ContentId = contentId.Trim();
            AllowsCashOut = allowsCashOut;
            _rewards = new List<LevelUpSystem.Reward>(rewards.Count);
            for (int i = 0; i < rewards.Count; i++)
            {
                if (rewards[i] == null)
                {
                    throw new ArgumentException(
                        $"固定升级 Offer {ContentId} 包含空候选。",
                        nameof(rewards));
                }
                _rewards.Add(rewards[i].Clone());
            }
        }

        /// <summary>
        /// Candidate objects carry mutable runtime card references, so a
        /// generated selection receives a fresh shell every time instead of
        /// sharing an offer author's collection.
        /// </summary>
        public List<LevelUpSystem.Reward> CreateRewardCopies()
        {
            var result = new List<LevelUpSystem.Reward>(_rewards.Count);
            for (int i = 0; i < _rewards.Count; i++)
                result.Add(_rewards[i].Clone());
            return result;
        }
    }

    /// <summary>
    /// Narrow Core-side lookup for authored deterministic offers.  Returning
    /// false with a null error means the reached level has no fixed offer and
    /// LevelUpSystem should use its ordinary random rule.  A non-empty error
    /// is a malformed content boundary and must stop the encounter instead of
    /// silently changing an authored reward table.
    /// </summary>
    public interface ILevelUpRewardOfferResolver
    {
        bool TryResolve(
            int reachedLevel,
            out LevelUpRewardOffer offer,
            out string error);
    }
}
