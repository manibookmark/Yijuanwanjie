using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Explicit collisions and pickups; completed destruction never hides the uncollected bracer.</summary>
    internal static class AgentGameAbandonedCarts
    {
        internal static bool IsCart(LevelNode node) =>
            node?.completionEffects?.Any(e => e?.kind == LevelInteractionEffectKind.AbandonedCart) == true;

        internal static bool TryAppend(DungeonRunSession run, LevelNode node, List<AgentGameCommand> commands, Action persist)
        {
            if (!IsCart(node)) return false;
            var state = run.CurrentFloor.GetNodeState(node.contentId);
            if (state != LevelNodeViewState.Available && state != LevelNodeViewState.Completed) return true;
            if (persist == null) throw new ArgumentNullException(nameof(persist));
            if (!run.AbandonedCarts.TryInspect(node.contentId, out var offer, out string error))
                throw new InvalidOperationException(error);
            string prefix = "abandoned-cart/" + node.contentId + "/";
            if (offer.CanBump)
            {
                int revision = offer.AcceptedBumps;
                commands.Add(new AgentGameCommand(new AgentGameAction {
                    actionId = prefix + "bump/" + revision.ToString(CultureInfo.InvariantCulture),
                    kind = "BumpAbandonedCart", nodeContentId = node.contentId, title = "撞击弃车",
                    description = $"有效撞击 {revision + 1}/4；第四次完成弃车节点并掉落护腕，另行拾取后本局标准抽牌数增加 1。"
                }, () => {
                    if (!run.AbandonedCarts.TryBump(node.contentId, revision, out _, out string failure, persist))
                        throw new InvalidOperationException(failure);
                }));
            }
            else if (offer.CanCollect)
                commands.Add(new AgentGameCommand(new AgentGameAction {
                    actionId = prefix + "collect", kind = "CollectCartBracer", nodeContentId = node.contentId,
                    title = "拾取护腕", description = "本局后续战斗每回合标准抽牌数增加 1；当前战斗不即时抽牌。"
                }, () => {
                    if (!run.AbandonedCarts.TryCollect(node.contentId, out _, out string failure, persist))
                        throw new InvalidOperationException(failure);
                }));
            return true;
        }

        internal static void InspectCoverage(DungeonRunSession run, LevelNode node, out bool canAct, out bool complete, out string reason)
        {
            canAct = complete = false; reason = null;
            var state = run.CurrentFloor.GetNodeState(node.contentId);
            if (state != LevelNodeViewState.Available && state != LevelNodeViewState.Completed) return;
            if (!run.AbandonedCarts.TryInspect(node.contentId, out var offer, out string error))
            { reason = error; return; }
            canAct = offer.CanBump || offer.CanCollect;
            complete = offer.Stage == CartStage.Collected;
            reason = offer.CanCollect ? "弃车已击碎，护腕尚未领取。" :
                offer.CanBump ? $"已接受 {offer.AcceptedBumps}/4 次撞击。" : null;
        }
    }
}
