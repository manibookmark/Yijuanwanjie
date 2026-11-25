using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Content
{
    /// <summary>
    /// Candidate type in an authored level-up schedule.  The profile stores
    /// only stable content IDs; Bootstrap resolves cards and Lingyin assets at
    /// the Content/Core boundary.
    /// </summary>
    public enum LevelUpOfferScheduleCandidateKind
    {
        Card,
        Seal
    }

    [Serializable]
    public sealed class LevelUpOfferScheduleCandidate
    {
        public LevelUpOfferScheduleCandidateKind kind;
        public string contentId;
    }

    /// <summary>
    /// One deterministic group that appears when the Run has reached a given
    /// level.  Candidate order is author-visible and is preserved exactly by
    /// the resolver; display text is resolved later and is never an ID.
    /// </summary>
    [Serializable]
    public sealed class LevelUpOfferScheduleEntry
    {
        public string offerContentId;
        [Min(2)] public int reachedLevel;
        [Tooltip("VC evidence did not establish an abandon branch for this fixed group. Keep false unless independently verified.")]
        public bool allowCashOut;
        public List<LevelUpOfferScheduleCandidate> candidates =
            new List<LevelUpOfferScheduleCandidate>();
    }

    /// <summary>
    /// Stable, reusable schedule for a level's deterministic reward offers.
    /// It deliberately contains no runtime card instances, player state, or
    /// presentation state, so Combat and map-interaction assembly can share
    /// the same source without duplicating the VC table.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewLevelUpOfferSchedule",
        menuName = "万界长卷/固定升级 Offer 排程")]
    public sealed class LevelUpOfferScheduleProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        [Tooltip("稳定升级 Offer 排程 ID。")]
        public string contentId;
        public List<LevelUpOfferScheduleEntry> offers =
            new List<LevelUpOfferScheduleEntry>();

        public List<string> ValidateProfile()
        {
            var errors = new List<string>();
            if (schemaVersion != CurrentSchemaVersion)
            {
                errors.Add(
                    $"schemaVersion 必须为 {CurrentSchemaVersion}。" );
            }
            if (string.IsNullOrWhiteSpace(contentId))
                errors.Add("固定升级 Offer 排程 contentId 不能为空。");
            if (offers == null || offers.Count == 0)
            {
                errors.Add("固定升级 Offer 排程至少需要一条记录。");
                return errors;
            }

            var levels = new HashSet<int>();
            var offerIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int offerIndex = 0; offerIndex < offers.Count; offerIndex++)
            {
                LevelUpOfferScheduleEntry offer = offers[offerIndex];
                if (offer == null)
                {
                    errors.Add($"offers[{offerIndex}] 不能为空。");
                    continue;
                }
                if (offer.reachedLevel < 2)
                {
                    errors.Add(
                        $"offers[{offerIndex}].reachedLevel 必须不小于 2。" );
                }
                else if (!levels.Add(offer.reachedLevel))
                {
                    errors.Add(
                        $"固定升级 Offer 的 reachedLevel 重复：{offer.reachedLevel}。" );
                }
                if (string.IsNullOrWhiteSpace(offer.offerContentId))
                {
                    errors.Add(
                        $"offers[{offerIndex}].offerContentId 不能为空。" );
                }
                else if (!offerIds.Add(offer.offerContentId))
                {
                    errors.Add(
                        $"固定升级 Offer contentId 重复：{offer.offerContentId}。" );
                }
                if (offer.candidates == null || offer.candidates.Count < 2)
                {
                    errors.Add(
                        $"固定升级 Offer {offer.offerContentId} 至少需要两个候选。" );
                    continue;
                }

                var candidateIds = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                for (int candidateIndex = 0;
                     candidateIndex < offer.candidates.Count;
                     candidateIndex++)
                {
                    LevelUpOfferScheduleCandidate candidate =
                        offer.candidates[candidateIndex];
                    if (candidate == null)
                    {
                        errors.Add(
                            $"固定升级 Offer {offer.offerContentId} 的候选 " +
                            $"{candidateIndex} 不能为空。" );
                        continue;
                    }
                    if (!Enum.IsDefined(
                            typeof(LevelUpOfferScheduleCandidateKind),
                            candidate.kind))
                    {
                        errors.Add(
                            $"固定升级 Offer {offer.offerContentId} 的候选 " +
                            $"{candidateIndex} 类型无效。" );
                    }
                    if (string.IsNullOrWhiteSpace(candidate.contentId))
                    {
                        errors.Add(
                            $"固定升级 Offer {offer.offerContentId} 的候选 " +
                            $"{candidateIndex} 缺少稳定 contentId。" );
                    }
                    else if (!candidateIds.Add(
                                 candidate.kind + "/" + candidate.contentId))
                    {
                        errors.Add(
                            $"固定升级 Offer {offer.offerContentId} 包含重复候选：" +
                            $"{candidate.contentId}。" );
                    }
                }
            }
            return errors;
        }
    }

    /// <summary>按稳定 ID 查找 Resources 内的固定升级 Offer 排程。</summary>
    public static class LevelUpOfferScheduleProfileCatalog
    {
        private const string ResourceDirectory = "Content/LevelUpOffers";

        public static LevelUpOfferScheduleProfile Get(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                return null;

            LevelUpOfferScheduleProfile[] profiles =
                Resources.LoadAll<LevelUpOfferScheduleProfile>(
                    ResourceDirectory);
            LevelUpOfferScheduleProfile match = null;
            for (int i = 0; i < profiles.Length; i++)
            {
                LevelUpOfferScheduleProfile profile = profiles[i];
                if (profile == null || !string.Equals(
                        profile.contentId,
                        contentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"固定升级 Offer 排程 contentId 重复：{contentId}。" );
                }
                match = profile;
            }
            return match;
        }
    }
}
