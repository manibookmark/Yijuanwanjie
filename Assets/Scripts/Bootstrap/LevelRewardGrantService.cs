using System;
using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Scene persistence adapter. Permanent reward validation and mutation live
    /// in Progression's shared rules; this existing facade preserves callers.
    /// </summary>
    public sealed class LevelRewardGrantService
    {
        private readonly LevelRewardGrantRules _rules;
        private readonly MetaProgression _account;
        private readonly Action<MetaProgression, string> _saveRun;
        private readonly GameSession _gameSession;

        public LevelRewardGrantService(MetaProgression meta, Action<MetaProgression> save,
            Action<MetaProgression, string> saveRun = null, GameSession gameSession = null)
        {
            _account = meta ?? throw new ArgumentNullException(nameof(meta));
            _rules = new LevelRewardGrantRules(_account, save ?? throw new ArgumentNullException(nameof(save)));
            _saveRun = saveRun;
            _gameSession = gameSession;
        }

        public IReadOnlyList<LevelRewardReceipt> Apply(IReadOnlyList<LevelRewardGrant> grants)
            => _rules.Apply(grants);

        public bool RecordLevelCleared(string levelContentId) => _rules.RecordLevelCleared(levelContentId);

        /// <summary>
        /// Restores presentation receipts only for a victory already committed
        /// in the active Run. The cached settlement preserves the frozen reward
        /// facts and persists them without granting anything a second time.
        /// </summary>
        public bool TryRecoverSettledVictory(DungeonRunSession run,
            out DungeonRunResultSnapshot result,
            out IReadOnlyList<LevelRewardReceipt> receipts, out string error)
        {
            result = null;
            receipts = Array.Empty<LevelRewardReceipt>();
            if (run == null || !run.TryGetConclusion(out DungeonRunResultSnapshot frozen) ||
                frozen.Status != DungeonRunResultStatus.Victory)
            {
                error = "Victory recovery requires an already settled active Run.";
                return false;
            }
            return TrySettleDungeon(run, out result, out receipts, out error);
        }

        /// <summary>
        /// Commits the same complete settlement as a headless Run, persisting
        /// account and Run in one payload before presentation. A failed save
        /// restores both rule owners and the existing balance view for retry.
        /// </summary>
        public bool TrySettleDungeon(DungeonRunSession run, out DungeonRunResultSnapshot result,
            out IReadOnlyList<LevelRewardReceipt> directReceipts, out string error)
        {
            result = null;
            directReceipts = Array.Empty<LevelRewardReceipt>();
            if (_saveRun == null) { error = "Dungeon settlement requires a combined account and Run save callback."; return false; }
            int previousBalance = _gameSession?.PlayerLingyu ?? 0;
            IReadOnlyList<LevelRewardReceipt> committedReceipts = null;
            bool settled = DungeonRunSettlement.TrySettleVictory(run, _account, out result, out error, () =>
            {
                if (!run.TryGetConclusion(out DungeonRunResultSnapshot frozen))
                    throw new InvalidOperationException("The settlement save requires a frozen result.");
                committedReceipts = BuildPresentationReceipts(run, frozen);
                if (_gameSession != null) _gameSession.PlayerLingyu = _account.lingyu;
                _saveRun(_account, run.Serialize());
            });
            if (!settled)
            {
                if (_gameSession != null) _gameSession.PlayerLingyu = previousBalance;
                return false;
            }
            directReceipts = committedReceipts;
            return true;
        }

        private static IReadOnlyList<LevelRewardReceipt> BuildPresentationReceipts(
            DungeonRunSession run, DungeonRunResultSnapshot result)
        {
            var frozen = new Dictionary<string, DungeonRunRewardResult>(StringComparer.OrdinalIgnoreCase);
            foreach (DungeonRunRewardResult reward in result.Rewards) frozen.Add(reward.GrantContentId, reward);
            var receipts = new List<LevelRewardReceipt>();
            foreach (LevelNode node in run.CurrentFloor.Nodes)
            {
                if (node == null || node.nodeType != LevelNodeType.Finale ||
                    run.CurrentFloor.GetNodeState(node.contentId) != LevelNodeViewState.Completed) continue;
                foreach (LevelRewardGrant grant in LevelRewardGrantRules.GetAutomaticGrants(node))
                {
                    if (!frozen.TryGetValue(grant.contentId, out DungeonRunRewardResult reward) ||
                        !string.Equals(reward.TargetContentId, grant.targetContentId, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The configured reward is absent from the frozen conclusion: " + grant.contentId);
                    receipts.Add(new LevelRewardReceipt(reward.GrantContentId, grant.kind, reward.TargetContentId,
                        reward.DisplayName, reward.NewlyGranted, grant.presentationId));
                }
            }
            return receipts.AsReadOnly();
        }
    }
}
