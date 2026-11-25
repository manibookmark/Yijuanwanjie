using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    public sealed class RunCardConversionCandidate
    {
        public int RuntimeInstanceId { get; }
        public string ContentId { get; }
        public RunCardConversionOutcome Outcome { get; }
        public int Amount { get; }

        internal RunCardConversionCandidate(CardConfig card, RunCardConversionRule rule, int amount)
        {
            RuntimeInstanceId = card.runtimeInstanceId;
            ContentId = card.contentId;
            Outcome = rule.outcome;
            Amount = amount;
        }
    }

    public sealed class RunCardConversionOffer
    {
        public IReadOnlyList<RunCardConversionCandidate> Candidates { get; }
        public int CashOutBaseJade { get; }
        public bool AllowsCashOut { get; }
        public bool AllowsLeave { get; }
        public bool Completed { get; }
        public LevelInteractionReceipt Receipt { get; }

        internal RunCardConversionOffer(List<RunCardConversionCandidate> candidates,
            int cashOutBaseJade, RunCardConversionRuleProfile profile,
            bool completed = false, LevelInteractionReceipt receipt = default)
        {
            Candidates = candidates.AsReadOnly();
            CashOutBaseJade = profile.AllowsCashOut ? cashOutBaseJade : 0;
            AllowsCashOut = !completed && profile.AllowsCashOut;
            AllowsLeave = !completed && profile.AllowsLeave;
            Completed = completed;
            Receipt = receipt;
        }
    }

    /// <summary>
    /// Owns conversion offers and their exact-instance/cash-out dispositions.
    /// The existing Run conversion transaction still owns card removal and stat
    /// changes. Inspect is read-only; rejected choices never consume a node.
    /// </summary>
    public static class RunCardConversionChoices
    {
        public static bool TryValidateProfile(LevelInteractionEffect effect,
            RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool, out string error)
        {
            error = null;
            if (effect == null || effect.kind != LevelInteractionEffectKind.ConvertCard ||
                string.IsNullOrWhiteSpace(effect.effectId) || effect.amount < 0 || effect.amountMax != 0)
            { error = "Conversion requires a configured effect and a nonnegative fixed cash-out amount."; return false; }
            if (profile == null || !string.Equals(effect.targetContentId, profile.contentId, StringComparison.OrdinalIgnoreCase))
            { error = "Conversion effect does not match its rule profile."; return false; }
            List<string> errors = profile.ValidateProfile(cardPool);
            if (errors.Count == 0) return true;
            error = string.Join(" | ", errors);
            return false;
        }

        /// <summary>Returns owned display copies of legal persistent instances; callers must destroy the copies.</summary>
        public static bool TryGetCandidateCards(DungeonRunSession run, LevelInteractionEffect effect,
            RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            out List<CardConfig> candidates, out string error)
        {
            candidates = new List<CardConfig>();
            if (!TryValidateProfile(effect, profile, cardPool, out error)) return false;
            if (run == null) { error = "Missing Run."; return false; }
            // An empty/unseeded Run has no payment; it must still be possible
            // to inspect an optional shrine and leave without receiving anything.
            if (run.RunCardCount == 0) return true;
            if (!run.TryGetCurrentCards(cardPool, out List<CardConfig> cards, out error)) return false;
            foreach (CardConfig card in cards)
            {
                if (profile.TryResolveRule(card, out RunCardConversionRule rule) &&
                    GetEffectiveAmount(run, rule) > 0) candidates.Add(card);
                else if (card != null) UnityEngine.Object.DestroyImmediate(card);
            }
            return true;
        }

        public static bool TryInspect(DungeonRunSession run, string nodeId, LevelInteractionEffect effect,
            RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            out RunCardConversionOffer offer, out string error)
            => TryInspectCore(null, run, nodeId, effect, profile, cardPool, out offer, out error);

        internal static bool TryInspectForCommit(RunCardConversionTransaction owner,
            DungeonRunSession run, string nodeId, LevelInteractionEffect effect,
            RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            out RunCardConversionOffer offer, out string error)
        {
            offer = null;
            if (owner == null || !ReferenceEquals(run?.CardConversions, owner) ||
                !owner.CanInspectOwnCommit(run))
            { error = "Complete the current Run reward transaction first."; return false; }
            return TryInspectCore(owner, run, nodeId, effect, profile, cardPool, out offer, out error);
        }

        private static bool TryInspectCore(RunCardConversionTransaction owner,
            DungeonRunSession run, string nodeId, LevelInteractionEffect effect,
            RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            out RunCardConversionOffer offer, out string error)
        {
            offer = null;
            if (!TryRequireNode(owner, run, nodeId, effect, out error) ||
                !TryValidateProfile(effect, profile, cardPool, out error)) return false;
            var candidates = new List<RunCardConversionCandidate>();
            if (run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out LevelInteractionReceipt prior))
            {
                offer = new RunCardConversionOffer(candidates, effect.amount, profile, true, prior);
                return true;
            }
            if (!TryGetCandidateCards(run, effect, profile, cardPool, out List<CardConfig> cards, out error)) return false;
            try
            {
                foreach (CardConfig card in cards)
                {
                    profile.TryResolveRule(card, out RunCardConversionRule rule);
                    candidates.Add(new RunCardConversionCandidate(card, rule, GetEffectiveAmount(run, rule)));
                }
                candidates.Sort((a, b) => a.RuntimeInstanceId.CompareTo(b.RuntimeInstanceId));
                offer = new RunCardConversionOffer(candidates, effect.amount, profile);
                return true;
            }
            finally { foreach (CardConfig card in cards) UnityEngine.Object.DestroyImmediate(card); }
        }

        public static bool TryConvert(DungeonRunSession run, string nodeId, LevelInteractionEffect effect,
            int runtimeInstanceId, RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            out LevelInteractionReceipt receipt, out string error, Action persist = null)
        {
            receipt = default;
            if (!TryInspect(run, nodeId, effect, profile, cardPool, out RunCardConversionOffer offer, out error)) return false;
            if (offer.Completed)
                return run.TryConvertCard(nodeId, effect, runtimeInstanceId, profile, cardPool, out receipt, out error, persist);
            bool offered = false;
            foreach (RunCardConversionCandidate candidate in offer.Candidates)
                if (candidate.RuntimeInstanceId == runtimeInstanceId) { offered = true; break; }
            if (!offered) { error = "Conversion target is absent from the current legal offer."; return false; }
            return run.TryConvertCard(nodeId, effect, runtimeInstanceId, profile, cardPool, out receipt, out error, persist);
        }

        public static bool TryCashOut(DungeonRunSession run, string nodeId, LevelInteractionEffect effect,
            RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            IDungeonRunInteractionWallet wallet, out LevelInteractionReceipt receipt, out string error,
            Action persist = null)
        {
            receipt = default;
            if (run == null) { error = "Conversion requires a Run."; return false; }
            return run.CardConversions.TryCashOut(nodeId, effect, profile, cardPool,
                wallet, out receipt, out error, persist);
        }

        private static bool TryRequireNode(RunCardConversionTransaction owner, DungeonRunSession run, string nodeId,
            LevelInteractionEffect effect, out string error)
        {
            if (run != null && (run.ChickenOfferings.IsCommitting || run.ChickenOfferings.HasPendingChoice))
            { error = "Complete the current Run reward transaction first."; return false; }
            bool ownsCommit = owner != null && ReferenceEquals(run?.CardConversions, owner) &&
                owner.CanInspectOwnCommit(run);
            if ((owner != null && !ownsCommit) || (!ownsCommit && run?.IsRewardCommitInProgress == true))
            { error = "Complete the current Run reward transaction first."; return false; }
            if (!TryValidateNodeEffect(run?.CurrentFloor, nodeId, effect, out error)) return false;
            if (run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out _)) return true;
            LevelNodeViewState state = run.CurrentFloor.GetNodeState(nodeId);
            if ((state != LevelNodeViewState.Available && state != LevelNodeViewState.Completing) ||
                run.CurrentFloor.ActiveEncounter.HasValue || run.CurrentFloor.TryGetPendingReturn(out _) ||
                run.Interactions.HasPendingChoice ||
                (!string.IsNullOrEmpty(run.CurrentFloor.ActiveInteractionNodeId) && run.CurrentFloor.ActiveInteractionNodeId != nodeId))
            { error = "Conversion node is not currently available."; return false; }
            return true;
        }

        // Both direct Run commits and inspected choices must prove the same
        // configured effect, including retries after the instance has gone.
        internal static bool TryValidateNodeEffect(LevelRunSession floor, string nodeId,
            LevelInteractionEffect effect, out string error)
        {
            error = null;
            LevelNode node = floor?.FindNode(nodeId);
            LevelInteractionEffect configured = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
            if (node == null || node.nodeType != LevelNodeType.Interaction || configured == null || effect == null ||
                configured.kind != LevelInteractionEffectKind.ConvertCard || configured.kind != effect.kind ||
                configured.effectId != effect.effectId ||
                configured.targetContentId != effect.targetContentId || configured.amount != effect.amount ||
                configured.amountMax != effect.amountMax)
            { error = "Conversion offer does not match the configured node effect."; return false; }
            return true;
        }

        /// <summary>The same capped gain is used by offer, preview and commit.</summary>
        public static int GetEffectiveAmount(DungeonRunSession run, RunCardConversionRule rule)
        {
            if (run?.CurrentFloor == null || rule == null) return 0;
            LevelRunModifierSnapshot modifiers = run.CurrentFloor.Modifiers;
            int current = rule.outcome switch
            {
                RunCardConversionOutcome.BaseArmorPerTurn => modifiers.BaseArmorPerTurnBonus,
                RunCardConversionOutcome.Might => modifiers.MightBonus,
                RunCardConversionOutcome.BaseMana => modifiers.BaseManaBonus,
                RunCardConversionOutcome.Area => modifiers.AreaBonus,
                RunCardConversionOutcome.Luck => run.RunProgression.RunLuckBonus,
                _ => int.MaxValue
            };
            return rule.GetEffectiveAmount(current);
        }
    }
}
