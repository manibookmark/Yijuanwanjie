using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Simulation.Protocol;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Detached public values only. Deck membership is sorted by instance;
    /// draw order, hidden waves and unrevealed rewards never enter this DTO.</summary>
    [Serializable] public sealed class AgentGameObservation
    {
        public string testCaseId, floorContentId, activeNodeContentId, phase, outcome, terminationReason;
        public string frozenEncounterFingerprint;
        public AgentGamePlayer player;
        public List<AgentGameCard> deck = new List<AgentGameCard>();
        public List<AgentGameNode> nodes = new List<AgentGameNode>();
        public List<AgentGameNode> coverage = new List<AgentGameNode>();
        public AgentGameBattle battle;
        public AgentGameOffer offer;
        public AgentGameSettlement settlement;
        public AgentGameCampaignAccountSnapshot account;
        public AgentGameCampaignState campaign;
    }
    [Serializable] public sealed class AgentGamePlayer
    {
        public int health, maximumHealth, armor, mana, temporaryMana, baseMana, level, experience, requiredExperience;
        public int might, amount, area, duration, standardDraw, drawBonus, luck, greed, growth, revival, curse, jade;
        public int baseManaBonus, mightBonus, areaBonus, baseArmorPerTurnBonus,
            standardDrawBonus, rewardRerollsRemaining;
        public bool cardPlayGrowthEnabled;
        public long totalRealPlays;
        public int cardPlayGrowthCycles, cardPlayGrowthBaseManaBonus;
        public List<string> unlockedRelics = new List<string>();
    }
    [Serializable] public sealed class AgentGameNode
    {
        public string floorContentId, nodeContentId, kind, state, coverage, reason;
        public bool executable;
        public List<string> knownEffects = new List<string>();
        public List<AgentGameEffect> effects = new List<AgentGameEffect>();
    }
    [Serializable] public sealed class AgentGameEffect
    { public string kind, summary, detail, targetContentId; public int amount, maximumAmount; }
    [Serializable] public sealed class AgentGameOffer
    {
        public string kind, nodeContentId, phase, title, message;
        public AgentGameSeal selectedSeal;
        public AgentGameCrystalState crystal;
        public List<AgentGameOption> options = new List<AgentGameOption>();
    }
    [Serializable] public sealed class AgentGameOption
    {
        public string choiceId, title, description, contentId, kind;
        public int runtimeInstanceId, secondaryRuntimeInstanceId, amount;
        public AgentGameCard card;
        public AgentGameSeal seal;
    }
    [Serializable] public sealed class AgentGameSeal
    {
        public string contentId, effect;
        public int value;
        public bool comboScalable;
        internal static AgentGameSeal Copy(CardSealSpec seal) => seal == null ? null : new AgentGameSeal {
            contentId = seal.contentId, effect = seal.effect.ToString(), value = seal.value, comboScalable = seal.comboScalable };
    }
    [Serializable] public sealed class AgentGameCard
    {
        public int runtimeInstanceId, baseManaValue, comboValue, paymentCost, baseDamage, armor, heal, manaGain;
        public int projectileCount, directTargetsPerHit, gemSlots, limitBreakCount, displayedDamage;
        public string contentId, name, description, detailDescription, color, costType, attackPattern, targetPolicy;
        public bool freeToPlay, wild, mild, comboBreaker, reverse, destroyOnUse;
        public List<AgentGameSeal> seals = new List<AgentGameSeal>();
        // Exact authored effect specifications, serialized from pure value specs,
        // not a CardConfig/Unity object or a calculated substitute rule.
        public List<string> effectSpecifications = new List<string>();
        public AgentGameComboPreview comboPreview;
        public bool triggersWuxing;
        public string wuxingFrom, wuxingTo;
        public List<AgentGameDamagePreview> damagePreview = new List<AgentGameDamagePreview>();
        public AgentGameSupportPreview supportEffects;
        public AgentGameDamageCoverage damageCoverage;
        public AgentGameImmediateDamage immediateDamage;
        internal static AgentGameCard Copy(CardConfig c, CombatController combat = null, BattleObservation.CardView view = null)
        {
            if (c == null) return null;
            ResolvedCardCost cost = c.ResolveCost();
            var result = new AgentGameCard {
                runtimeInstanceId = c.runtimeInstanceId, contentId = c.contentId, name = c.cardName,
                description = c.description, detailDescription = c.detailDescription, color = c.color.ToString(),
                costType = c.costType.ToString(), baseManaValue = cost.BaseManaValue, comboValue = cost.ComboValue,
                paymentCost = cost.PaymentCost, freeToPlay = cost.IsFreeToPlay, wild = cost.IsWild,
                mild = cost.IsMild, comboBreaker = cost.IsComboBreaker, reverse = cost.HasReverse,
                baseDamage = c.baseDamage,
                projectileCount = c.projectileCount, directTargetsPerHit = c.directTargetsPerHit,
                gemSlots = c.gemSlots, limitBreakCount = c.LimitBreakCount, destroyOnUse = c.destroyOnUse,
                attackPattern = c.attackPattern.ToString(), targetPolicy = c.targetPolicy.ToString(),
                displayedDamage = view?.DisplayedDamage ?? c.baseDamage
            };
            if (c.seals != null) result.seals.AddRange(c.seals.Where(s => s != null).Select(AgentGameSeal.Copy));
            if (c.effects != null) result.effectSpecifications.AddRange(c.effects.Where(e => e != null).Select(e => JsonUtility.ToJson(e)));
            if (combat != null)
            {
                ComboPreview preview = combat.GetComboPreview(c);
                result.comboPreview = new AgentGameComboPreview { wouldCombo = preview.WouldCombo,
                    wouldBreak = preview.WouldBreak, wouldClearCombo = preview.WouldClearCombo,
                    resultingCombo = preview.ResultingCombo, resultingMultiplier = preview.ResultingMultiplier, description = preview.Description };
                result.triggersWuxing = combat.TryResolveWuxingPairForPreview(c, out CardColor from, out CardColor to);
                if (result.triggersWuxing) { result.wuxingFrom = from.ToString(); result.wuxingTo = to.ToString(); }
            }
            CardSupportEffectPreview support = view?.SupportEffects ?? CardSupportEffectProjection.Project(
                c, result.comboPreview?.resultingMultiplier ?? 1, combat?.State.Mana);
            // Compatibility scalars are typed-first host bases, never actual settlement deltas.
            result.armor = support.BaseArmor; result.heal = support.BaseHeal; result.manaGain = support.BaseManaGain;
            result.supportEffects = AgentGameSupportPreview.Copy(support);
            result.damageCoverage = AgentGameDamageCoverage.Copy(view?.DamageCoverage ??
                (combat != null ? combat.GetDamagePreviewCoverage(c) :
                    CardPlayDamagePreview.DescribeCoverage(c, null)));
            if (view != null) result.damagePreview.AddRange(view.DamagePreview.Select(p => new AgentGameDamagePreview {
                enemyRuntimeId = p.EnemyRuntimeId, damage = p.Damage, willKill = p.WillKill,
                isEstimate = p.IsEstimate, estimatedWillKill = p.EstimatedWillKill }));
            result.immediateDamage = AgentGameImmediateDamage.Copy(view?.ImmediateDamage);
            return result;
        }
    }

    [Serializable] public sealed class AgentGameImmediateDamage
    {
        public bool hasCombatContext, hasDamage, isEstimate;
        public int totalAppliedDamage;
        public List<AgentGameImmediateTargetDamage> targets = new List<AgentGameImmediateTargetDamage>();

        internal static AgentGameImmediateDamage Copy(BattleObservation.ImmediateDamageView source)
        {
            var result = new AgentGameImmediateDamage {
                hasCombatContext = source?.HasCombatContext == true,
                hasDamage = source?.HasDamage == true,
                isEstimate = source == null || source.IsEstimate,
                totalAppliedDamage = source?.TotalAppliedDamage ?? 0
            };
            if (source != null)
                result.targets.AddRange(source.Targets.Select(row => new AgentGameImmediateTargetDamage {
                    enemyRuntimeId = row.EnemyRuntimeId, appliedDamage = row.AppliedDamage,
                    overkillDamage = row.OverkillDamage, isErased = row.IsErased,
                    isEstimate = row.IsEstimate, willKill = row.WillKill
                }));
            return result;
        }
    }

    [Serializable] public sealed class AgentGameImmediateTargetDamage
    {
        public int enemyRuntimeId, appliedDamage, overkillDamage;
        public bool isErased, isEstimate, willKill;
    }
    [Serializable] public sealed class AgentGameSupportCondition
    { public string kind, stringValue; public int intValue; public float floatValue; }
    [Serializable] public sealed class AgentGameSupportEffect
    {
        public string effectType, statType, target, source;
        public int sourceIndex, baseAmount, baseCount, scaledAmount, scaledCount, paidManaMultiplier;
        public bool comboScalable, conditionsKnown, conditionsSatisfied;
        public List<AgentGameSupportCondition> conditions = new List<AgentGameSupportCondition>();
    }
    [Serializable] public sealed class AgentGameSupportPreview
    {
        public int resultingMultiplier, baseArmor, baseHeal, baseManaGain, scaledArmor, scaledHeal, scaledManaGain;
        public string coverage, limitations;
        public List<AgentGameSupportEffect> effects = new List<AgentGameSupportEffect>();
        internal static AgentGameSupportPreview Copy(CardSupportEffectPreview source)
        {
            var result = new AgentGameSupportPreview {
                resultingMultiplier = source.ResultingMultiplier, baseArmor = source.BaseArmor,
                baseHeal = source.BaseHeal, baseManaGain = source.BaseManaGain,
                scaledArmor = source.ScaledArmor, scaledHeal = source.ScaledHeal, scaledManaGain = source.ScaledManaGain,
                coverage = source.Coverage, limitations = source.Limitations
            };
            foreach (CardSupportEffectEntry effect in source.Effects)
                result.effects.Add(new AgentGameSupportEffect {
                    effectType = effect.EffectType.ToString(), statType = effect.StatType.ToString(),
                    target = effect.Target.ToString(), source = effect.Source.ToString(), sourceIndex = effect.SourceIndex,
                    baseAmount = effect.BaseAmount, baseCount = effect.BaseCount,
                    scaledAmount = effect.ScaledAmount, scaledCount = effect.ScaledCount,
                    comboScalable = effect.ComboScalable, paidManaMultiplier = effect.PaidManaMultiplier,
                    conditionsKnown = effect.ConditionsSatisfied.HasValue,
                    conditionsSatisfied = effect.ConditionsSatisfied ?? false,
                    conditions = effect.Conditions.Select(c => new AgentGameSupportCondition {
                        kind = c.Kind, intValue = c.IntValue, floatValue = c.FloatValue, stringValue = c.StringValue
                    }).ToList()
                });
            return result;
        }
    }
    [Serializable] public sealed class AgentGameDamageCoverage
    {
        public bool isEstimate, hasCombatContext, hasUnresolvedConditions, hasUnresolvedRandomness,
            hasPotentialDirectDamage, emptyPreviewGuaranteesNoDamage;
        public string coverage, limitations;
        internal static AgentGameDamageCoverage Copy(CardDamagePreviewCoverage source) => new AgentGameDamageCoverage {
            isEstimate = source.IsEstimate, hasCombatContext = source.HasCombatContext,
            hasUnresolvedConditions = source.HasUnresolvedConditions,
            hasUnresolvedRandomness = source.HasUnresolvedRandomness,
            hasPotentialDirectDamage = source.HasPotentialDirectDamage,
            emptyPreviewGuaranteesNoDamage = source.EmptyPreviewGuaranteesNoDamage,
            coverage = source.Coverage, limitations = source.Limitations
        };
    }
    [Serializable] public sealed class AgentGameComboPreview
    {
        public bool wouldCombo, wouldBreak, wouldClearCombo;
        public int resultingCombo, resultingMultiplier;
        public string description;
    }
    [Serializable] public sealed class AgentGameDamagePreview
    {
        public int enemyRuntimeId, damage;
        // A random/control-free calculation is advisory, never a guaranteed kill.
        public bool willKill, isEstimate, estimatedWillKill;
    }
    [Serializable] public sealed class AgentGameStatus { public string effect; public int stacks, remainingTurns, sourceId; }
    [Serializable] public sealed class AgentGameEnemy
    {
        public int runtimeId, health, maximumHealth, armor, row, column, gridWidth, gridDepth, intentAmount, intentTargetCount;
        public string contentId, name, description, intent, intentDescription;
        public bool boss, disarmed, overkillTarget;
        public List<AgentGameStatus> statuses = new List<AgentGameStatus>();
    }
    [Serializable] public sealed class AgentGameCrawler
    { public int runtimeId, remainingTriggers; public string contentId, name, triggerColor, triggerEffect; }
    [Serializable] public sealed class AgentGameTerrain
    { public int runtimeId, sourceCardInstanceId, column, row, magnitudePerStack, stacks, remainingTriggers; public string effectId, effectKind, triggerTiming, damageKind; }
    [Serializable] public sealed class AgentGameBattle
    {
        public string phase, lastPlayedColor;
        public int turn, drawPileCount, discardPileCount, comboLength, outstandingWuxingBonus;
        public bool comboEnabled, comboActive, hasLastComboValue, allowReverseNext, chainLinkActive;
        public int lastComboValue;
        public List<int> chainSequence = new List<int>();
        public bool awaitingReward, sealAssignmentPending, cashOutAllowed;
        public List<AgentGameCard> hand = new List<AgentGameCard>();
        public List<AgentGameEnemy> enemies = new List<AgentGameEnemy>();
        public List<AgentGameCrawler> crawlers = new List<AgentGameCrawler>();
        public List<AgentGameStatus> playerStatuses = new List<AgentGameStatus>();
        public List<AgentGameTerrain> terrain = new List<AgentGameTerrain>();
        public int bossTotalEyes, bossOpenEyes, bossPressureDamage;
        public bool bossPressureActive, bossPressureTriggered;
        public bool waitingForTargets;
        public string targetAvailabilityMessage;

        internal static AgentGameBattle Copy(CombatController combat, HeadlessCombatSession headless)
        {
            BattleObservation source = headless.Observe();
            ComboSystem combo = combat.State.Combo;
            var result = new AgentGameBattle {
                phase = source.Phase.ToString(), turn = source.Turn, drawPileCount = source.DrawPileCount,
                discardPileCount = source.DiscardPileCount, comboLength = combo.ComboLength,
                comboEnabled = combo.Enabled, comboActive = combo.IsActive, hasLastComboValue = combo.LastComboValue.HasValue,
                lastComboValue = combo.LastComboValue ?? 0, allowReverseNext = combo.AllowReverseNext,
                outstandingWuxingBonus = combo.OutstandingWuxingBonus, chainLinkActive = combo.ChainLinkActive,
                chainSequence = new List<int>(combo.ChainSequence), lastPlayedColor = combat.LastPlayedCardColor.ToString(),
                awaitingReward = source.AwaitingRewardSelection, sealAssignmentPending = combat.HasPendingLevelUpSealAssignment,
                cashOutAllowed = combat.CanCashOutPendingRewards,
                waitingForTargets = source.WaitingForTargets, targetAvailabilityMessage = source.TargetAvailabilityMessage
            };
            foreach (BattleObservation.CardView card in source.Hand)
                result.hand.Add(AgentGameCard.Copy(combat.Hand.First(c => c.runtimeInstanceId == card.RuntimeInstanceId), combat, card));
            foreach (BattleObservation.EnemyView e in source.VisibleEnemies)
                result.enemies.Add(new AgentGameEnemy { runtimeId = e.RuntimeId, contentId = e.ContentId, name = e.Name,
                    description = e.Description, health = e.CurrentHealth, maximumHealth = e.MaxHealth, armor = e.Armor,
                    row = e.Row, column = e.Column, gridWidth = e.GridWidth, gridDepth = e.GridDepth, boss = e.IsBoss,
                    disarmed = e.IsDisarmed, overkillTarget = e.IsOverkillTarget, intent = e.Intent.Type.ToString(),
                    intentAmount = e.Intent.Amount, intentTargetCount = e.Intent.TargetCount, intentDescription = e.Intent.Description,
                    statuses = CopyStatuses(e.Statuses) });
            result.crawlers.AddRange(source.ActiveCrawlers.Select(c => new AgentGameCrawler { runtimeId = c.RuntimeId,
                contentId = c.ContentId, name = c.Name, triggerColor = c.TriggerColor.ToString(), triggerEffect = c.TriggerEffect,
                remainingTriggers = c.RemainingTriggers }));
            result.playerStatuses = CopyStatuses(source.PlayerStatuses);
            result.terrain.AddRange(source.TerrainEffects.Select(t => new AgentGameTerrain { runtimeId = t.RuntimeId,
                effectId = t.EffectId, effectKind = t.EffectKind.ToString(), triggerTiming = t.TriggerTiming.ToString(),
                damageKind = t.DamageKind.ToString(), sourceCardInstanceId = t.SourceCardInstanceId, column = t.Column,
                row = t.Row, magnitudePerStack = t.MagnitudePerStack, stacks = t.Stacks, remainingTriggers = t.RemainingTriggers }));
            if (source.BossTimer != null) { result.bossPressureActive = true; result.bossTotalEyes = source.BossTimer.TotalEyes;
                result.bossOpenEyes = source.BossTimer.OpenEyes; result.bossPressureDamage = source.BossTimer.Damage;
                result.bossPressureTriggered = source.BossTimer.IsTriggered; }
            return result;
        }
        private static List<AgentGameStatus> CopyStatuses(IReadOnlyList<BattleObservation.StatusView> source)
            => source.Select(s => new AgentGameStatus { effect = s.Effect.ToString(), stacks = s.Stacks,
                remainingTurns = s.RemainingTurns, sourceId = s.SourceId }).ToList();
    }
}
