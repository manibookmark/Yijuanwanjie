using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Resolves a Finale's versioned random seal pool at the combat-return
    /// boundary.  The resolver owns no Run state: it receives the stable Run
    /// seed/luck snapshot and returns the exact candidate IDs that the floor
    /// reward state machine will persist.
    /// </summary>
    internal static class DungeonRunVictorySealRewardResolver
    {
        internal static bool TryResolve(
            string runId,
            int runSeed,
            int luck,
            LevelRewardGrant grant,
            IReadOnlyList<DungeonRunSealRewardPool> pools,
            out IReadOnlyList<string> sealContentIds,
            out string error,
            IReadOnlyList<CardConfig> eligibleHosts = null)
        {
            sealContentIds = Array.Empty<string>();
            error = null;
            if (grant == null ||
                string.IsNullOrWhiteSpace(grant.sealRewardPoolId))
            {
                error = "终局奖励未声明随机灵印池。";
                return false;
            }
            if (grant.sealChoiceCount <= 0 ||
                grant.sealChoiceCountMax < grant.sealChoiceCount)
            {
                error = "终局随机灵印候选数量范围无效。";
                return false;
            }

            DungeonRunSealRewardPool pool = null;
            foreach (DungeonRunSealRewardPool candidate in
                     pools ?? Array.Empty<DungeonRunSealRewardPool>())
            {
                if (candidate != null && string.Equals(
                        candidate.ContentId,
                        grant.sealRewardPoolId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    pool = candidate;
                    break;
                }
            }
            if (pool == null)
            {
                error = "终局随机灵印池尚未由内容装配层配置：" +
                    grant.sealRewardPoolId;
                return false;
            }

            var candidates = new List<CardSealSpec>();
            var candidateIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (CardSealSpec candidate in
                     pool.Candidates ?? Array.Empty<CardSealSpec>())
            {
                if (candidate == null ||
                    string.IsNullOrWhiteSpace(candidate.contentId) ||
                    !Enum.IsDefined(typeof(CardSealEffect), candidate.effect) ||
                    candidate.value <= 0 ||
                    !candidateIds.Add(candidate.contentId))
                {
                    error = "终局随机灵印池包含无效或重复候选。";
                    return false;
                }
                candidates.Add(candidate);
            }
            var preferredIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string preferredId in
                     pool.PreferredCandidateContentIds ??
                     Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(preferredId) ||
                    !preferredIds.Add(preferredId) ||
                    !candidateIds.Contains(preferredId))
                {
                    error = "终局随机灵印池的优先候选不属于完整候选池。";
                    return false;
                }
            }
            if (pool.PreferredCandidateCount < 0)
            {
                error = "终局随机灵印池的优先候选数量不能为负数。";
                return false;
            }

            // Account eligibility belongs to the assembled pool; host legality
            // belongs to Core. Filter before rolling, so an unusable preferred
            // seal never displaces a candidate that can actually be installed.
            if (eligibleHosts != null)
                candidates.RemoveAll(seal => !eligibleHosts.Any(host =>
                    host != null && !host.IsEncounterTemporary &&
                    CardSealRules.CanAssign(host, seal,
                        isEvolutionResultEligible: pool.IsEvolutionResultEligible)));
            if (candidates.Count == 0)
            {
                // An explicitly resolved empty pool is distinct from a missing
                // or invalid pool. The floor freezes a cash-out-only transaction.
                if (grant.allowCashOut) return true;
                error = "当前账号没有可进入终局高阶印匣的已解锁合法灵印，且奖励不允许化玉。";
                return false;
            }

            unchecked
            {
                uint hash = 2166136261;
                string seedText = (runId ?? string.Empty) + "/" +
                    runSeed + "/" + grant.contentId + "/" +
                    pool.ContentId;
                foreach (char value in seedText)
                    hash = (hash ^ value) * 16777619;
                var random = new SeededRandomSource((int)(hash & int.MaxValue));
                var rewardConfig = new RewardConfig
                {
                    affectedByLuck = true,
                    luckAffectsChoiceCount = true,
                    baseChoiceCount = grant.sealChoiceCount,
                    maxChoiceCount = grant.sealChoiceCountMax,
                    fourChoiceLuckK = 100f
                };
                int count = Math.Min(
                    candidates.Count,
                    Math.Max(1, rewardConfig.RollChoiceCount(luck, random)));
                var preferred = candidates.Where(candidate =>
                    preferredIds.Contains(candidate.contentId)).ToList();
                random.Shuffle(preferred);
                int preferredCount = Math.Min(
                    Math.Max(0, pool.PreferredCandidateCount),
                    Math.Min(count, preferred.Count));
                var selected = new List<CardSealSpec>(count);
                selected.AddRange(preferred.Take(preferredCount));
                var selectedIds = new HashSet<string>(
                    selected.Select(candidate => candidate.contentId),
                    StringComparer.OrdinalIgnoreCase);
                var remaining = candidates.Where(candidate =>
                    !selectedIds.Contains(candidate.contentId)).ToList();
                random.Shuffle(remaining);
                selected.AddRange(remaining.Take(count - selected.Count));
                sealContentIds = selected.Select(candidate => candidate.contentId)
                    .ToList().AsReadOnly();
                return true;
            }
        }
    }
}
