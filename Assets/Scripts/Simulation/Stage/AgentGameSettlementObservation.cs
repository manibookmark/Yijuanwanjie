using System;
using System.Collections.Generic;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Detached receipt projection. Totals describe already earned Run
    /// income; final-action entries describe only credits committed by that action.
    /// Neither reading nor closing this receipt can credit an account.</summary>
    [Serializable] public sealed class AgentGameSettlement
    {
        public string status, runId, dungeonContentId, floorContentId;
        public int collectedJade, levelBonusJade, dungeonBonusJade, finalActionCollectedJade;
        public long totalEarnedJade, finalActionEarnedJade;
        public List<AgentGameJadeReward> jadeRewards = new List<AgentGameJadeReward>();
        public List<AgentGameJadeReward> finalActionJadeRewards = new List<AgentGameJadeReward>();
        public List<AgentGameSettlementUnlock> unlockRewards = new List<AgentGameSettlementUnlock>();
    }

    [Serializable] public sealed class AgentGameJadeReward
    {
        public string kind, scope;
        public int amount;
    }

    [Serializable] public sealed class AgentGameSettlementUnlock
    {
        public string grantContentId, kind, category, targetContentId, displayName;
        public bool newlyGranted;
    }

    /// <summary>Values at the beginning of a caller's action, read from the
    /// authoritative Run. An existing bonus is never attributed to a later action.</summary>
    internal readonly struct AgentGameSettlementBoundary
    {
        internal readonly int CollectedJade;
        internal readonly bool LevelBonusGranted, DungeonBonusGranted;

        internal AgentGameSettlementBoundary(DungeonRunSession run)
        {
            CollectedJade = run.RunProgression.RunJadeEarned;
            LevelBonusGranted = run.IsLevelBonusGranted;
            DungeonBonusGranted = run.IsDungeonBonusGranted;
        }
    }

    /// <summary>Owns the public meaning of formal settlement receipts. The Run
    /// remains the sole owner of settlement, grants and conclusion state; this
    /// module only copies its frozen result and a pre-action receipt boundary.</summary>
    internal static class AgentGameSettlementObservation
    {
        internal static AgentGameSettlement Copy(DungeonRunSession run,
            AgentGameSettlementBoundary boundary)
        {
            if (!run.TryGetConclusion(out DungeonRunResultSnapshot result)) return null;
            if (result.CollectedJade < boundary.CollectedJade)
                throw new InvalidOperationException("Settlement income precedes the action's authoritative Run receipt.");
            var value = new AgentGameSettlement {
                status = result.Status.ToString(), runId = result.RunId,
                dungeonContentId = result.DungeonContentId, floorContentId = result.FloorContentId,
                collectedJade = result.CollectedJade,
                levelBonusJade = result.LevelBonusJade, dungeonBonusJade = result.DungeonBonusJade,
                finalActionCollectedJade = result.CollectedJade - boundary.CollectedJade,
                totalEarnedJade = (long)result.CollectedJade + result.LevelBonusJade + result.DungeonBonusJade
            };
            Add(value.jadeRewards, "CollectedJade", "run-total", result.CollectedJade);
            // Defeat is a frozen gameplay result, but never a victory bonus.
            if (result.Status == DungeonRunResultStatus.Victory)
            {
                Add(value.jadeRewards, "LevelBonus", "run-total", result.LevelBonusJade);
                Add(value.jadeRewards, "DungeonBonus", "run-total", result.DungeonBonusJade);
            }
            Add(value.finalActionJadeRewards, "CollectedJade", "final-action", value.finalActionCollectedJade);
            if (result.Status == DungeonRunResultStatus.Victory)
            {
                if (!boundary.LevelBonusGranted)
                    Add(value.finalActionJadeRewards, "LevelBonus", "final-action", result.LevelBonusJade);
                if (!boundary.DungeonBonusGranted)
                    Add(value.finalActionJadeRewards, "DungeonBonus", "final-action", result.DungeonBonusJade);
            }
            foreach (AgentGameJadeReward reward in value.finalActionJadeRewards)
                value.finalActionEarnedJade += reward.amount;
            foreach (DungeonRunRewardResult reward in result.Rewards)
                value.unlockRewards.Add(new AgentGameSettlementUnlock {
                    grantContentId = reward.GrantContentId, kind = reward.Kind.ToString(),
                    category = reward.Category.ToString(), targetContentId = reward.TargetContentId,
                    displayName = reward.DisplayName, newlyGranted = reward.NewlyGranted
                });
            return value;
        }

        private static void Add(List<AgentGameJadeReward> rewards, string kind, string scope, int amount)
        {
            if (amount > 0) rewards.Add(new AgentGameJadeReward { kind = kind, scope = scope, amount = amount });
        }
    }
}
