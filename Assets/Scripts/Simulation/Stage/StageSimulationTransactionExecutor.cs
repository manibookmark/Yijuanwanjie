using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;
using WanjieScroll.Simulation.Agents;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns deterministic, non-UI settlement for one typed simulation route.
    /// It reuses Dungeon's authoritative Run transactions and hides stable-card
    /// targeting, temporary card pools, interaction receipts, and pending seal
    /// choices from both checkpoint and batch callers.
    /// Scale review: all methods still implement this single settlement boundary;
    /// injected random/reward strategies are the only execution inputs. The
    /// multi-step map offer loop and its temporary content lifetime belong to
    /// StageSimulationInteractionRunner; all Run mutations remain in Dungeon.
    /// Re-review when this class reaches 1,000 lines or adds an effect family
    /// with independent state; do not extract pass-through helpers for line count.
    /// </summary>
    internal sealed class StageSimulationTransactionExecutor
    {
        private readonly IRandomSource _random;
        private readonly ISimulationRewardPolicy _rewardPolicy;
        private readonly MetaProgression _rewardAccount;

        internal StageSimulationTransactionExecutor(
            IRandomSource random,
            ISimulationRewardPolicy rewardPolicy = null,
            MetaProgression rewardAccount = null)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _rewardPolicy = rewardPolicy ?? new StandardRewardPolicy();
            _rewardAccount = rewardAccount;
        }

        /// <summary>The final route ledger must include rewards committed after its last combat.</summary>
        internal static IReadOnlyList<StageCardSnapshot> CaptureRunDeck(DungeonRunSession dungeon)
        {
            if (dungeon.RunCardCount == 0) return Array.Empty<StageCardSnapshot>();
            List<CardConfig> pool = ContentRuntimeFactory.CreateCardPool(
                ResolveFloorVolumeId(dungeon.CurrentFloor));
            try { return CaptureRunDeck(dungeon, pool); }
            finally { DestroyCards(pool); }
        }

        internal static IReadOnlyList<StageCardSnapshot> CaptureRunDeck(
            DungeonRunSession dungeon, IReadOnlyList<CardConfig> cardPool)
        {
            if (dungeon.RunCardCount == 0) return Array.Empty<StageCardSnapshot>();
            if (!dungeon.TryGetCurrentCards(cardPool, out List<CardConfig> cards, out string error))
                throw new InvalidOperationException("Cannot capture final Run cards: " + error);
            try
            {
                var snapshots = new List<StageCardSnapshot>();
                foreach (CardConfig card in cards)
                    snapshots.Add(new StageCardSnapshot(card.runtimeInstanceId, card.contentId));
                snapshots.Sort((left, right) => left.RuntimeInstanceId.CompareTo(right.RuntimeInstanceId));
                return snapshots.AsReadOnly();
            }
            finally { DestroyCards(cards); }
        }

        internal DungeonRunFloorExitResult ExecuteInteraction(
            DungeonRunSession dungeon,
            StageSimulationStep step,
            string expectedFloorId = null,
            Func<string, LevelRunProfile> resolveFloor = null)
        {
            if (dungeon == null) throw new ArgumentNullException(nameof(dungeon));
            if (step == null) throw new ArgumentNullException(nameof(step));
            dungeon.ConfigureSealRewardEligibility(() =>
                SealRewardEligibility.CanReceiveSeals(_rewardAccount));
            expectedFloorId ??= dungeon.CurrentFloorContentId;
            // Consult the root transaction before looking at the current map:
            // a retry can refer to a completed Exit on the preceding floor.
            if (step.Kind == StageSimulationStepKind.Exit)
            {
                if (!dungeon.FloorExits.TryCommit(expectedFloorId, step.NodeContentId,
                        resolveFloor ?? LevelRunProfileCatalog.Get,
                        StageSimulationInteractionRunner.CreateExitWallet(dungeon),
                        out DungeonRunFloorExitResult result, out string exitError))
                    throw new InvalidOperationException("Cannot commit simulation Exit: " + exitError);
                return result;
            }
            if (!string.Equals(expectedFloorId, dungeon.CurrentFloorContentId, StringComparison.Ordinal))
                throw new InvalidOperationException("Simulation step belongs to a departed floor: " + expectedFloorId);
            if (step.Kind != StageSimulationStepKind.Interaction &&
                step.Kind != StageSimulationStepKind.Exit)
            {
                throw new InvalidOperationException(
                    $"Step {step.NodeContentId} is not an interaction or Exit.");
            }

            LevelNode node = dungeon.CurrentFloor.FindNode(
                step.NodeContentId);
            if (node == null || node.nodeType != LevelNodeType.Interaction)
            {
                throw new InvalidOperationException(
                    "Simulation interaction step must target an Interaction (use an Exit step for Exit): " +
                    step.NodeContentId + ".");
            }
            if (node.completionEffects != null &&
                node.completionEffects.Exists(effect => effect != null &&
                    DungeonRunInteractionTransaction.Handles(effect.kind)))
            {
                StageSimulationInteractionRunner.Execute(
                    dungeon, step, _rewardPolicy, _rewardAccount);
                return null;
            }
            if (!dungeon.CurrentFloor.TryBeginInteraction(
                    step.NodeContentId,
                    out string beginError))
            {
                throw new InvalidOperationException(
                    $"Cannot begin simulation interaction " +
                    $"{step.NodeContentId}: {beginError}");
            }

            if (node.completionEffects != null)
            {
                for (int i = 0; i < node.completionEffects.Count; i++)
                {
                    LevelInteractionEffect effect = node.completionEffects[i]
                        ?? throw new InvalidOperationException(
                            $"Interaction {step.NodeContentId} contains a null " +
                            "effect.");
                    ApplyInteractionEffect(dungeon, node, effect, step);
                }
            }
            if (dungeon.CurrentFloor.GetNodeState(step.NodeContentId) != LevelNodeViewState.Completed &&
                !dungeon.CurrentFloor.TryCompleteInteraction(
                    step.NodeContentId,
                    out string completeError))
            {
                throw new InvalidOperationException(
                    $"Cannot complete simulation interaction " +
                    $"{step.NodeContentId}: {completeError}");
            }
            return null;
        }

        internal void ResolvePendingVictorySeal(
            DungeonRunSession dungeon,
            BattleScenarioProfile scenario,
            StageSimulationStep step,
            IReadOnlyList<CardConfig> availableCards = null)
        {
            if (dungeon == null) throw new ArgumentNullException(nameof(dungeon));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (step == null) throw new ArgumentNullException(nameof(step));

            bool hasPending = dungeon.CurrentFloor.TryGetPendingVictorySealReward(
                out LevelVictorySealRewardOffer offer);
            if (!hasPending)
            {
                if (step.VictorySealDisposition !=
                    StageSimulationSealDisposition.None)
                {
                    throw new InvalidOperationException(
                        $"Combat {step.NodeContentId} declares a victory seal " +
                        "disposition, but production content granted none.");
                }
                return;
            }
            if (step.VictorySealDisposition ==
                StageSimulationSealDisposition.None)
            {
                throw new InvalidOperationException(
                    $"Combat {step.NodeContentId} granted pending seal " +
                    $"{offer.SealContentId}; the route must declare Assign " +
                    "or CashOut.");
            }
            if (!string.Equals(offer.NodeContentId, step.NodeContentId, StringComparison.Ordinal))
                throw new InvalidOperationException("Pending victory reward belongs to another node.");
            if (offer.IsCashOutOnly)
            {
                if (step.VictorySealDisposition != StageSimulationSealDisposition.CashOut ||
                    !string.IsNullOrWhiteSpace(step.VictorySealContentId))
                    throw new InvalidOperationException(
                        "An empty-pool reward only permits CashOut without a seal identity.");
                CashOutVictorySeal(dungeon, offer);
                return;
            }
            string selectedSealId = ResolveVictorySealIdentity(offer.SealContentIds, step.VictorySealContentId);
            if (!string.IsNullOrEmpty(offer.SelectedSealContentId) &&
                !string.Equals(offer.SelectedSealContentId, selectedSealId, StringComparison.Ordinal))
                throw new InvalidOperationException("The restored victory seal selection differs from the route decision.");
            if (step.VictorySealDisposition ==
                StageSimulationSealDisposition.CashOut)
            {
                if (!offer.AllowsCashOut)
                    throw new InvalidOperationException("The victory seal reward does not allow CashOut.");
                CommitVictorySealChoice(dungeon, offer, selectedSealId);
                if (offer.Phase == LevelVictorySealRewardPhase.AssigningSeal &&
                    !dungeon.CurrentFloor.TryReturnToVictorySealDisposition(offer.RewardContentId, out string returnError))
                    throw new InvalidOperationException(returnError);
                CashOutVictorySeal(dungeon, offer);
                return;
            }

            CardSealSpec seal = ResolveSeal(selectedSealId);
            // The optional pool is the same in-memory content boundary used by contract tests;
            // only a pool materialized here is owned and released by this adapter.
            IReadOnlyList<CardConfig> cardPool = availableCards ??
                ContentRuntimeFactory.CreateCardPool(scenario.volumeId);
            try
            {
                int targetId = ResolveTargetCardInstanceId(
                    dungeon,
                    cardPool,
                    step.TargetCardContentId,
                    step.TargetCardOccurrence,
                    card => CardSealRules.CanAssign(card, seal,
                        isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult),
                    $"cannot host seal {seal.contentId}");
                CommitVictorySealChoice(dungeon, offer, selectedSealId);
                if (!dungeon.CurrentFloor.TryBeginVictorySealAssignment(
                        offer.RewardContentId,
                        out string beginError))
                {
                    throw new InvalidOperationException(
                        "Cannot begin victory seal assignment " +
                        $"{offer.RewardContentId}: {beginError}");
                }
                if (!dungeon.TryAssignVictorySealReward(
                        offer.RewardContentId,
                        targetId,
                        seal,
                        cardPool,
                        out _,
                        out string assignError,
                        isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult))
                {
                    throw new InvalidOperationException(
                        $"Cannot assign victory seal {offer.RewardContentId}: " +
                        assignError);
                }
            }
            finally
            {
                if (availableCards == null) DestroyCards(cardPool);
            }
        }

        private static string ResolveVictorySealIdentity(IReadOnlyList<string> candidates, string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                if (candidates == null || candidates.Count != 1)
                    throw new InvalidOperationException("A multi-seal reward requires explicit victorySealContentId.");
                return candidates[0];
            }
            if (candidates != null)
                foreach (string id in candidates)
                    if (string.Equals(id, requested, StringComparison.Ordinal)) return id;
            throw new InvalidOperationException("victorySealContentId is not offered by this reward: " + requested);
        }

        private static void CommitVictorySealChoice(DungeonRunSession dungeon,
            LevelVictorySealRewardOffer offer, string selectedSealId)
        {
            if (offer.Phase == LevelVictorySealRewardPhase.ChoosingSeal &&
                !dungeon.CurrentFloor.TryChooseVictorySeal(offer.RewardContentId, selectedSealId, out string error))
                throw new InvalidOperationException(error);
        }

        internal static bool TryGetSupportBlocker(
            LevelNode node,
            StageSimulationStep step,
            bool hasLaterCombat,
            out string blocker)
        {
            blocker = null;
            if (node == null)
            {
                blocker = "node does not exist.";
                return true;
            }
            if (step == null)
            {
                blocker = "step is null.";
                return true;
            }

            switch (step.Kind)
            {
                case StageSimulationStepKind.Combat:
                    if (node.nodeType != LevelNodeType.Encounter &&
                        node.nodeType != LevelNodeType.Finale)
                    {
                        blocker = $"node type is {node.nodeType}, not combat.";
                        return true;
                    }
                    return TryGetVictoryRewardBlocker(
                        node,
                        step,
                        hasLaterCombat,
                        out blocker);

                case StageSimulationStepKind.Interaction:
                    if (node.nodeType != LevelNodeType.Interaction)
                    {
                        blocker =
                            $"node type is {node.nodeType}, not Interaction.";
                        return true;
                    }
                    return TryGetInteractionBlocker(node, step, out blocker);

                case StageSimulationStepKind.Exit:
                    if (node.nodeType != LevelNodeType.Exit)
                    {
                        blocker = $"node type is {node.nodeType}, not Exit.";
                        return true;
                    }
                    return TryGetInteractionBlocker(node, step, out blocker);

                default:
                    blocker = "unsupported step kind " + step.Kind + ".";
                    return true;
            }
        }

        private static bool TryGetVictoryRewardBlocker(
            LevelNode node,
            StageSimulationStep step,
            bool hasLaterCombat,
            out string blocker)
        {
            blocker = null;
            bool hasSealChoice = false;
            LevelRewardGrant sealReward = null;
            if (node.victoryRewards != null)
            {
                for (int i = 0; i < node.victoryRewards.Count; i++)
                {
                    LevelRewardGrant reward = node.victoryRewards[i];
                    if (reward == null) continue;
                    if (string.Equals(
                            reward.kind.ToString(),
                            "SealChoice",
                            StringComparison.Ordinal))
                    {
                        hasSealChoice = true;
                        sealReward = reward;
                        continue;
                    }
                    if (reward.kind != LevelRewardKind.CrawlerUnlock &&
                        reward.kind != LevelRewardKind.RelicUnlock)
                    {
                        blocker =
                            $"unsupported victory reward kind {reward.kind}.";
                        return true;
                    }
                    if (hasLaterCombat)
                    {
                        blocker =
                            "CrawlerUnlock before later combat is outside the " +
                            "Run-only simulation ledger.";
                        return true;
                    }
                }
            }

            if (hasSealChoice && step.VictorySealDisposition ==
                    StageSimulationSealDisposition.None)
            {
                blocker = "SealChoice requires Assign or CashOut.";
                return true;
            }
            if (!hasSealChoice && step.VictorySealDisposition !=
                    StageSimulationSealDisposition.None)
            {
                blocker =
                    "the step declares a seal disposition but content has no " +
                    "SealChoice reward.";
                return true;
            }
            if (sealReward != null)
            {
                try
                {
                    ResolveVictorySealIdentity(
                        sealReward.sealChoiceContentIds != null && sealReward.sealChoiceContentIds.Count > 0
                            ? (IReadOnlyList<string>)sealReward.sealChoiceContentIds
                            : new[] { sealReward.targetContentId },
                        step.VictorySealContentId);
                    if (step.VictorySealDisposition == StageSimulationSealDisposition.CashOut && !sealReward.allowCashOut)
                        throw new InvalidOperationException("The victory seal reward does not allow CashOut.");
                }
                catch (InvalidOperationException exception)
                {
                    blocker = exception.Message;
                    return true;
                }
            }
            return false;
        }

        private static bool TryGetInteractionBlocker(
            LevelNode node,
            StageSimulationStep step,
            out string blocker)
        {
            blocker = null;
            LevelInteractionEffect managedEffect = node.completionEffects?.Find(
                effect => effect != null &&
                    DungeonRunInteractionTransaction.Handles(effect.kind));
            if (managedEffect != null)
            {
                if (node.completionEffects.Count != 1)
                    blocker = "Run interaction transactions require exactly one effect.";
                else if (step.CardConversionDisposition !=
                         StageSimulationCardConversionDisposition.None)
                    blocker = "Run interaction transactions cannot use ConvertCard dispositions.";
                else if (!string.IsNullOrWhiteSpace(step.TargetCardContentId) &&
                         managedEffect.kind != LevelInteractionEffectKind.RefineCard &&
                         managedEffect.kind != LevelInteractionEffectKind.ChooseSeal &&
                         managedEffect.kind != LevelInteractionEffectKind.GemChest)
                    blocker = "This Run interaction has no targeted card choice.";
                return blocker != null;
            }
            bool grantsSeal = false;
            bool convertsCard = false;
            if (node.completionEffects != null)
            {
                for (int i = 0; i < node.completionEffects.Count; i++)
                {
                    LevelInteractionEffect effect = node.completionEffects[i];
                    if (effect == null)
                    {
                        blocker = "contains a null interaction effect.";
                        return true;
                    }
                    switch (effect.kind)
                    {
                        case LevelInteractionEffectKind.AddBaseMana:
                        case LevelInteractionEffectKind.AddBaseArmor:
                        case LevelInteractionEffectKind.AddHandLimit:
                        case LevelInteractionEffectKind.AddMight:
                        case LevelInteractionEffectKind.AddArea:
                        case LevelInteractionEffectKind.AddRewardReroll:
                        case LevelInteractionEffectKind.RecordStoryClue:
                        case LevelInteractionEffectKind.GrantJade:
                        case LevelInteractionEffectKind.GrantRelic:
                        case LevelInteractionEffectKind.HealCurrentHealth:
                            break;
                        case LevelInteractionEffectKind.GrantSeal:
                            grantsSeal = true;
                            break;
                        case LevelInteractionEffectKind.ConvertCard:
                            convertsCard = true;
                            break;
                        default:
                            blocker =
                                $"unsupported interaction effect {effect.kind} " +
                                $"({effect.effectId}).";
                            return true;
                    }
                }
            }
            if (grantsSeal && convertsCard)
            {
                blocker =
                    "GrantSeal and ConvertCard cannot share one interaction.";
                return true;
            }
            if (convertsCard && step.CardConversionDisposition ==
                    StageSimulationCardConversionDisposition.None)
            {
                blocker =
                    "ConvertCard requires an explicit Convert or CashOut " +
                    "disposition.";
                return true;
            }
            if (!convertsCard && step.CardConversionDisposition !=
                    StageSimulationCardConversionDisposition.None)
            {
                blocker =
                    "the step declares a card conversion disposition but " +
                    "content has no ConvertCard effect.";
                return true;
            }

            bool requiresCardTarget = grantsSeal ||
                step.CardConversionDisposition ==
                    StageSimulationCardConversionDisposition.Convert;
            if (requiresCardTarget && string.IsNullOrWhiteSpace(
                    step.TargetCardContentId))
            {
                blocker =
                    "The interaction requires targetCardContentId and " +
                    "occurrence.";
                return true;
            }
            if (step.CardConversionDisposition ==
                    StageSimulationCardConversionDisposition.CashOut &&
                !string.IsNullOrWhiteSpace(step.TargetCardContentId))
            {
                blocker = "ConvertCard CashOut cannot declare a card target.";
                return true;
            }
            if (!requiresCardTarget && !string.IsNullOrWhiteSpace(
                    step.TargetCardContentId))
            {
                blocker =
                    "the step declares a card target but content has no " +
                    "targeted card effect.";
                return true;
            }
            return false;
        }

        // Dynamic Run callers use the same typed simple-effect boundary after
        // beginning an interaction; target choices remain explicit commands.
        internal void ApplyInteractionEffect(
            DungeonRunSession dungeon,
            LevelNode node,
            LevelInteractionEffect effect,
            StageSimulationStep step)
        {
            if (node.nodeType == LevelNodeType.Exit)
                throw new InvalidOperationException("Exit effects must be committed through FloorExits.");
            switch (effect.kind)
            {
                case LevelInteractionEffectKind.AddBaseMana:
                case LevelInteractionEffectKind.AddBaseArmor:
                case LevelInteractionEffectKind.AddHandLimit:
                case LevelInteractionEffectKind.AddMight:
                case LevelInteractionEffectKind.AddArea:
                case LevelInteractionEffectKind.AddRewardReroll:
                case LevelInteractionEffectKind.RecordStoryClue:
                    RequireRunEffect(
                        dungeon.CurrentFloor.TryApplyRunEffect(
                            node.contentId,
                            effect,
                            out _,
                            out string runEffectError),
                        effect,
                        runEffectError);
                    return;

                case LevelInteractionEffectKind.GrantJade:
                    int rolled = effect.amountMax > effect.amount
                        ? _random.Range(effect.amount, effect.amountMax + 1)
                        : effect.amount;
                    int before = dungeon.RunProgression.RunJadeEarned;
                    var economy = new EconomySystem(
                        new EconomyConfig(),
                        dungeon.RunProgression);
                    economy.Grant(
                        rolled,
                        dungeon.RunProgression.RunGreedBonus,
                        JadeSource.Special,
                        affectedByGreed: true);
                    int after = dungeon.RunProgression.RunJadeEarned;
                    RequireRunEffect(
                        dungeon.CurrentFloor.TryRecordExternalEffect(
                            node.contentId,
                            effect,
                            before,
                            after,
                            after - before,
                            out _,
                            out string jadeError),
                        effect,
                        jadeError);
                    return;

                case LevelInteractionEffectKind.GrantRelic:
                    // The isolated simulation account owns permanent feature
                    // permission. Later nodes must observe a relic acquired now.
                    if (_rewardAccount != null)
                    {
                        if (!ContentCatalog.Current.TryGetRelic(effect.targetContentId, out RelicData grantedRelic) ||
                            !Enum.IsDefined(typeof(RelicType), grantedRelic.relicType))
                            throw new InvalidOperationException("Missing or invalid granted relic: " + effect.targetContentId);
                        _rewardAccount.TryUnlockRelic(grantedRelic.contentId, (RelicType)grantedRelic.relicType,
                            grantedRelic.name, grantedRelic.description);
                    }
                    RequireRunEffect(
                        dungeon.CurrentFloor.TryRecordExternalEffect(
                            node.contentId,
                            effect,
                            0,
                            effect.amount,
                            effect.amount,
                            out _,
                            out string relicError),
                        effect,
                        relicError);
                    return;

                case LevelInteractionEffectKind.GrantSeal:
                    ApplyInteractionSeal(dungeon, node, effect, step);
                    return;

                case LevelInteractionEffectKind.HealCurrentHealth:
                    ApplyHeal(dungeon, node, effect);
                    return;

                case LevelInteractionEffectKind.ConvertCard:
                    ApplyCardConversion(dungeon, node, effect, step);
                    return;

                default:
                    throw new InvalidOperationException(
                        "Simulation does not support interaction effect " +
                        $"{effect.kind} ({effect.effectId}).");
            }
        }

        private static void ApplyCardConversion(
            DungeonRunSession dungeon,
            LevelNode node,
            LevelInteractionEffect effect,
            StageSimulationStep step)
        {
            if (string.IsNullOrWhiteSpace(step.TargetCardContentId))
            {
                if (step.CardConversionDisposition !=
                    StageSimulationCardConversionDisposition.CashOut)
                {
                    throw new InvalidOperationException(
                        $"Interaction {node.contentId} converts a card through " +
                        $"{effect.targetContentId}; the route must declare a " +
                        "stable targetCardContentId and occurrence.");
                }
            }
            else if (step.CardConversionDisposition !=
                     StageSimulationCardConversionDisposition.Convert)
            {
                throw new InvalidOperationException(
                    $"Interaction {node.contentId} declares a card target " +
                    "without selecting the Convert disposition.");
            }

            if (step.CardConversionDisposition ==
                StageSimulationCardConversionDisposition.None)
            {
                throw new InvalidOperationException(
                    $"Interaction {node.contentId} must explicitly select " +
                    "Convert or CashOut.");
            }

            RunCardConversionRuleProfile profile;
            try
            {
                profile = RunCardConversionRuleCatalog.Get(
                    effect.targetContentId);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Cannot resolve simulation card conversion " +
                    $"{effect.effectId}: {exception.Message}",
                    exception);
            }

            if (dungeon.CurrentFloor.TryGetInteractionReceipt(
                    effect.effectId,
                    out LevelInteractionReceipt previous))
            {
                bool previouslyConverted =
                    previous.TargetRuntimeInstanceId > 0;
                bool wantsConvert = step.CardConversionDisposition ==
                    StageSimulationCardConversionDisposition.Convert;
                if (previouslyConverted != wantsConvert)
                {
                    throw new InvalidOperationException(
                        $"Card conversion {effect.effectId} was already " +
                        $"settled as {(previouslyConverted ? "Convert" : "CashOut")}; " +
                        $"it cannot change to {step.CardConversionDisposition}.");
                }
                return;
            }

            if (step.CardConversionDisposition ==
                StageSimulationCardConversionDisposition.CashOut)
            {
                if (!profile.AllowsCashOut)
                    throw new InvalidOperationException(
                        "This conversion does not permit CashOut; a persistent card must be paid.");
                string cashOutVolumeId = ResolveFloorVolumeId(
                    dungeon.CurrentFloor);
                List<CardConfig> validationPool =
                    ContentRuntimeFactory.CreateCardPool(cashOutVolumeId);
                try
                {
                    List<string> validationErrors =
                        profile.ValidateProfile(validationPool);
                    if (validationErrors.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"Card conversion profile {profile.contentId} " +
                            "is invalid: " +
                            string.Join(" | ", validationErrors));
                    }
                    ApplyCardConversionCashOut(dungeon, node, effect);
                }
                finally
                {
                    DestroyCards(validationPool);
                }
                return;
            }

            string volumeId = ResolveFloorVolumeId(
                dungeon.CurrentFloor);
            List<CardConfig> cardPool =
                ContentRuntimeFactory.CreateCardPool(volumeId);
            try
            {
                int targetId = ResolveTargetCardInstanceId(
                    dungeon,
                    cardPool,
                    step.TargetCardContentId,
                    step.TargetCardOccurrence,
                    card => profile.TryResolveRule(card, out _),
                    $"is not eligible for conversion profile " +
                    $"{profile.contentId}");
                if (!dungeon.TryConvertCard(
                        node.contentId,
                        effect,
                        targetId,
                        profile,
                        cardPool,
                        out _,
                        out string conversionError))
                {
                    throw new InvalidOperationException(
                        $"Cannot convert simulation card for " +
                        $"{effect.effectId}: {conversionError}");
                }
            }
            finally
            {
                DestroyCards(cardPool);
            }
        }

        private static void ApplyCardConversionCashOut(
            DungeonRunSession dungeon,
            LevelNode node,
            LevelInteractionEffect effect)
        {
            int before = dungeon.RunProgression.RunJadeEarned;
            var economy = new EconomySystem(
                new EconomyConfig(),
                dungeon.RunProgression);
            economy.Grant(
                effect.amount,
                dungeon.RunProgression.RunGreedBonus,
                JadeSource.Special,
                affectedByGreed: true);
            int after = dungeon.RunProgression.RunJadeEarned;
            RequireRunEffect(
                dungeon.CurrentFloor.TryRecordExternalEffect(
                    node.contentId,
                    effect,
                    before,
                    after,
                    after - before,
                    out _,
                    out string receiptError),
                effect,
                receiptError);
        }

        private static void ApplyHeal(
            DungeonRunSession dungeon,
            LevelNode node,
            LevelInteractionEffect effect)
        {
            if (!dungeon.TryHealCurrentHealth(
                    effect.amount,
                    out int before,
                    out int after,
                    out string healError))
            {
                throw new InvalidOperationException(
                    $"Cannot apply simulation heal {effect.effectId}: " +
                    healError);
            }
            RequireRunEffect(
                dungeon.CurrentFloor.TryRecordExternalEffect(
                    node.contentId,
                    effect,
                    before,
                    after,
                    after - before,
                    out _,
                    out string receiptError),
                effect,
                receiptError);
        }

        private static void ApplyInteractionSeal(
            DungeonRunSession dungeon,
            LevelNode node,
            LevelInteractionEffect effect,
            StageSimulationStep step)
        {
            if (!dungeon.CanReceiveSealRewards)
            {
                RequireRunEffect(dungeon.TryCashOutSealReward(node.contentId, effect,
                    new EconomyConfig().cashOutJade,
                    StageSimulationInteractionRunner.CreateSealCashOutWallet(dungeon),
                    out _, out string cashOutError), effect, cashOutError);
                return;
            }
            if (string.IsNullOrWhiteSpace(step.TargetCardContentId))
            {
                throw new InvalidOperationException(
                    $"Interaction {node.contentId} grants seal " +
                    $"{effect.targetContentId}; the route must declare a " +
                    "stable targetCardContentId and occurrence.");
            }
            CardSealSpec seal = ResolveSeal(effect.targetContentId);
            string volumeId = ResolveFloorVolumeId(
                dungeon.CurrentFloor);
            List<CardConfig> cardPool =
                ContentRuntimeFactory.CreateCardPool(volumeId);
            try
            {
                int targetId = ResolveTargetCardInstanceId(
                    dungeon,
                    cardPool,
                    step.TargetCardContentId,
                    step.TargetCardOccurrence,
                    card => CardSealRules.CanAssign(card, seal,
                        isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult),
                    $"cannot host seal {seal.contentId}");
                if (!dungeon.TryAssignSeal(
                        node.contentId,
                        effect,
                        targetId,
                        seal,
                        cardPool,
                        out _,
                        out string assignError,
                        isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult))
                {
                    throw new InvalidOperationException(
                        $"Cannot assign interaction seal {effect.effectId}: " +
                        assignError);
                }
            }
            finally
            {
                DestroyCards(cardPool);
            }
        }

        private static void CashOutVictorySeal(
            DungeonRunSession dungeon,
            LevelVictorySealRewardOffer offer)
        {
            var economy = new EconomySystem(
                new EconomyConfig(),
                dungeon.RunProgression);
            if (!dungeon.TryCashOutVictorySealReward(
                    offer.RewardContentId,
                    () => economy.CashOut(
                        dungeon.RunProgression.RunGreedBonus),
                    out _,
                    out string error))
            {
                throw new InvalidOperationException(
                    $"Cannot cash out victory seal {offer.RewardContentId}: " +
                    error);
            }
        }

        private static int ResolveTargetCardInstanceId(
            DungeonRunSession dungeon,
            IReadOnlyList<CardConfig> cardPool,
            string contentId,
            int occurrence,
            Func<CardConfig, bool> isEligible,
            string ineligibleReason)
        {
            if (isEligible == null)
                throw new ArgumentNullException(nameof(isEligible));
            if (!dungeon.TryGetCurrentCards(
                    cardPool,
                    out List<CardConfig> cards,
                    out string cardsError))
            {
                throw new InvalidOperationException(
                    "Cannot inspect simulation Run cards: " + cardsError);
            }
            try
            {
                cards.Sort((left, right) =>
                    left.runtimeInstanceId.CompareTo(right.runtimeInstanceId));
                int seen = 0;
                for (int i = 0; i < cards.Count; i++)
                {
                    CardConfig card = cards[i];
                    if (card == null || !string.Equals(
                            card.contentId,
                            contentId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (seen++ != occurrence) continue;
                    if (!isEligible(card))
                    {
                        throw new InvalidOperationException(
                            $"Route target {contentId}[{occurrence}] " +
                            $"{ineligibleReason}.");
                    }
                    return card.runtimeInstanceId;
                }
                throw new InvalidOperationException(
                    $"Run deck does not contain target card " +
                    $"{contentId}[{occurrence}].");
            }
            finally
            {
                DestroyCards(cards);
            }
        }

        private static CardSealSpec ResolveSeal(string contentId)
        {
            if (!LingyinCatalog.TryGet(contentId, out LingyinConfig seal) ||
                seal == null)
            {
                throw new InvalidOperationException(
                    "Cannot resolve simulation seal content: " + contentId + ".");
            }
            if (!seal.IsValid(out string error))
            {
                throw new InvalidOperationException(
                    $"Simulation seal {contentId} is invalid: {error}");
            }
            return seal.ToCardSealSpec();
        }

        private static string ResolveFloorVolumeId(LevelRunSession floor)
        {
            var nodes = floor.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                LevelNode node = nodes[i];
                if (node == null ||
                    (node.nodeType != LevelNodeType.Encounter &&
                     node.nodeType != LevelNodeType.Finale))
                {
                    continue;
                }
                BattleScenarioProfile scenario = BattleScenarioCatalog.Get(
                    node.scenarioId);
                if (scenario != null &&
                    !string.IsNullOrWhiteSpace(scenario.volumeId))
                {
                    return scenario.volumeId;
                }
            }
            throw new InvalidOperationException(
                $"Floor {floor.Profile.contentId} has no combat scenario from which " +
                "to resolve its card pool volume.");
        }

        private static void RequireRunEffect(
            bool success,
            LevelInteractionEffect effect,
            string error)
        {
            if (success) return;
            throw new InvalidOperationException(
                $"Cannot apply simulation effect {effect.effectId}: {error}");
        }

        private static void DestroyCards(IReadOnlyList<CardConfig> cards)
        {
            if (cards == null) return;
            for (int i = 0; i < cards.Count; i++)
            {
                CardConfig card = cards[i];
                if (card != null) UnityEngine.Object.DestroyImmediate(card);
            }
        }
    }
}
