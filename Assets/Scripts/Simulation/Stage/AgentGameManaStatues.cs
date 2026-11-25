using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Maps the statue's authoritative phase to one explicit text command.
    /// Revision identity belongs to Dungeon, never to an automatic policy or
    /// a cached protocol counter. Entering a node does not perform three bumps
    /// or collect its released pickup. An explicit memory save is appropriate
    /// only for the isolated session; this adapter does not claim disk durability.
    /// </summary>
    internal static class AgentGameManaStatues
    {
        internal static bool TryAppend(DungeonRunSession run, LevelNode node,
            List<AgentGameCommand> commands, Action persist)
        {
            if (node.completionEffects?.Any(effect => effect?.kind == LevelInteractionEffectKind.ManaStatue) != true)
                return false;
            if (persist == null) throw new ArgumentNullException(nameof(persist));
            if (!run.ManaStatues.TryInspect(node.contentId, out var offer, out string error))
                throw new InvalidOperationException(error);
            string prefix = "mana-statue/" + node.contentId + "/";
            if (offer.CanBump)
            {
                int revision = offer.AcceptedBumps;
                commands.Add(new AgentGameCommand(new AgentGameAction {
                    actionId = prefix + "bump/" + revision.ToString(CultureInfo.InvariantCulture),
                    kind = "BumpManaStatue", nodeContentId = node.contentId, title = "撞击灵气神像",
                    description = $"有效撞击 {revision + 1}/3；第三次只释放拾取物，不自动取得灵气。"
                }, () => {
                    if (!run.ManaStatues.TryBump(node.contentId, revision, out _, out string failure, persist))
                        throw new InvalidOperationException(failure);
                }));
            }
            else if (offer.CanCollect)
            {
                commands.Add(new AgentGameCommand(new AgentGameAction {
                    actionId = prefix + "collect", kind = "CollectManaStatue", nodeContentId = node.contentId,
                    title = "拾取灵气", description = "拾取已释放的灵气；当前灵气加 1 后钳制至 100 万，本局基础灵气增加 1。"
                }, () => {
                    if (!run.ManaStatues.TryCollectAndComplete(node.contentId, out _, out string failure, persist))
                        throw new InvalidOperationException(failure);
                }));
            }
            return true;
        }
    }
}
