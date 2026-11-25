using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Projects the one Dungeon offering owner onto explicit public choices.
    /// Holds no mutable draft, payment, receipt or automatic decision. Observation
    /// only reads an already frozen choice; typed commands own every checkpoint.
    /// The isolated Agent session saves to owned memory, while callers can inject
    /// a strict persistence boundary for faults and durable host integration.</summary>
    internal static class AgentGameCardOfferings
    {
        internal static bool Handles(LevelNode node) => node?.completionEffects?.Any(effect =>
            effect?.kind == LevelInteractionEffectKind.ChickenOffering ||
            effect?.kind == LevelInteractionEffectKind.ManaOffering) == true;

        private static bool IsMana(LevelNode node) => node.completionEffects.Single().kind == LevelInteractionEffectKind.ManaOffering;

        internal static string Availability(DungeonRunSession run, LevelNode node,
            IChickenOfferingContext context, out string reason)
        {
            reason = null;
            if (node.completionEffects.Count != 1 || node.completionEffects[0] == null)
            { reason = "Offering requires one typed effect contract."; return "Unimplemented"; }
            if (IsMana(node))
            {
                if (!(context is IManaOfferingContext mana) || mana.QueryEligibility()?.IsUnlocked != true)
                { reason = "Mana Offering permanent qualification has not been obtained."; return "ConditionUnavailable"; }
                if (!run.HasManaState || !run.TryGetMapStartingMana(out _))
                { reason = "Mana Offering requires a known current and starting Mana balance."; return "ConditionUnavailable"; }
            }
            else if (context?.QueryEligibility()?.IsUnlocked != true)
            { reason = "Chicken Offering permanent qualification has not been obtained."; return "ConditionUnavailable"; }
            if (!run.HasPlayerState)
            { reason = "Offering requires an established Run player."; return "ConditionUnavailable"; }
            return null;
        }

        internal static void Begin(DungeonRunSession run, LevelNode node,
            IChickenOfferingContext context, Action persist)
        {
            DungeonRunChickenOfferingOffer offer; string error;
            bool opened = IsMana(node)
                ? run.CardOfferings.TryOpenMana(node.contentId, (IManaOfferingContext)context, out offer, out error, persist)
                : run.CardOfferings.TryOpen(node.contentId, context, out offer, out error, persist);
            AgentGameInteractions.Require(opened, error);
        }

        internal static AgentGameOffer Observe(DungeonRunSession run,
            IChickenOfferingContext context, IReadOnlyList<CardConfig> cardPool)
        {
            if (!run.CardOfferings.HasPendingChoice) return null;
            string nodeId = run.CardOfferings.PendingNodeContentId;
            LevelNode node = run.CurrentFloor.FindNode(nodeId);
            DungeonRunChickenOfferingOffer source; string error;
            // A persistence call here is a contract violation. No inspection
            // is allowed to open a new choice or replace the frozen instances.
            Action forbidWrite = () => throw new InvalidOperationException("Offering observation attempted a checkpoint.");
            bool read = IsMana(node)
                ? run.CardOfferings.TryOpenMana(nodeId, (IManaOfferingContext)context, out source, out error, forbidWrite)
                : run.CardOfferings.TryOpen(nodeId, context, out source, out error, forbidWrite);
            AgentGameInteractions.Require(read, error);
            AgentGameInteractions.Require(source.Pending && !source.HasReceipt, "Expected a frozen Offering choice.");
            AgentGameInteractions.Require(run.TryGetCurrentCards(cardPool, out var cards, out error), error);
            try
            {
                bool mana = source.Kind == CardOfferingKind.Mana;
                var result = new AgentGameOffer { kind = mana ? "ManaOffering" : "ChickenOffering",
                    nodeContentId = nodeId, phase = "CardChoice", title = "献出一张卡牌",
                    message = mana ? "销毁所选本局实例，当前与基础灵气各请求加1；实际余额受上限约束。"
                        : "销毁所选本局实例，领取3份食物。" };
                foreach (var candidate in source.Candidates)
                {
                    CardConfig card = cards.Single(item => item.runtimeInstanceId == candidate.RuntimeInstanceId);
                    result.options.Add(new AgentGameOption { choiceId = "offer/" + candidate.RuntimeInstanceId,
                        kind = "OfferCard", runtimeInstanceId = candidate.RuntimeInstanceId,
                        contentId = candidate.ContentId, title = card.cardName, card = AgentGameCard.Copy(card),
                        amount = mana ? ManaOfferingContracts.ManaAmount : ChickenOfferingContracts.FoodCount,
                        description = mana ? result.message : $"领取3份食物，每份恢复 {ChickenOfferingContracts.HealingPerFood(context.ExperimentalMedicineEnabled)} 点生命。" });
                }
                result.options.Add(new AgentGameOption { choiceId = "return-map", kind = "ReturnToMap",
                    title = "离开（未供奉，不领取奖励）" });
                return result;
            }
            finally { AgentGameInteractions.DestroyCards(cards); }
        }

        internal static void Commit(DungeonRunSession run, IChickenOfferingContext context,
            AgentGameOption option, Action persist)
        {
            AgentGameInteractions.Require(run.CardOfferings.HasPendingChoice, "No frozen Offering choice remains.");
            string nodeId = run.CardOfferings.PendingNodeContentId;
            if (option.choiceId == "return-map")
            { AgentGameInteractions.Require(run.CardOfferings.TryCancel(nodeId, out var cancelError, persist), cancelError); return; }
            AgentGameInteractions.Require(option.kind == "OfferCard" && option.choiceId == "offer/" + option.runtimeInstanceId,
                "Offering choice must identify one exact frozen instance.");
            LevelNode node = run.CurrentFloor.FindNode(nodeId);
            string error; LevelInteractionReceipt receipt;
            bool submitted = IsMana(node)
                ? run.CardOfferings.TrySubmitManaAndComplete(nodeId, option.runtimeInstanceId, (IManaOfferingContext)context, out receipt, out error, persist)
                : run.CardOfferings.TrySubmitAndComplete(nodeId, option.runtimeInstanceId, context, out receipt, out error, persist);
            AgentGameInteractions.Require(submitted, error);
        }
    }
}
