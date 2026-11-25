using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Simulation.Agents;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns build/exploration utility, never legality or game mutation. Values a
    /// changed deck's draw quality, cost links, color coverage and typed hero
    /// triggers; stable identity only breaks equal scores. No content-ID recipes.
    /// </summary>
    public sealed class RunAcceptancePolicy : ISimulationRewardPolicy
    {
        public const string PolicyId = "adaptive-run";
        public const int CurrentRevision = 2;
        private readonly RunAcceptanceObjective _objective;
        private readonly IReadOnlyList<CrawlerConfig> _heroes;
        private readonly CombatRuleSet _rules;
        public string Id => PolicyId;
        public int Revision => CurrentRevision;

        public RunAcceptancePolicy(RunAcceptanceObjective objective,
            IReadOnlyList<CrawlerConfig> heroes = null, CombatRuleSet rules = null)
        {
            _objective = objective;
            _heroes = heroes ?? Array.Empty<CrawlerConfig>();
            _rules = rules?.Clone() ?? new CombatRuleSet();
        }

        public int ChooseReward(CombatController combat, IReadOnlyList<LevelUpSystem.Reward> rewards)
        {
            if (combat == null || rewards == null || rewards.Count == 0)
                throw new ArgumentException("A live reward context and nonempty formal offers are required.");
            List<CardConfig> deck = combat.Deck.Concat(combat.Hand).Concat(combat.Discard).ToList();
            return ScoreRewards(combat.Player, deck, rewards).OrderByDescending(c => c.score)
                .ThenBy(c => c.instanceId).First().instanceId;
        }

        internal List<RunAcceptanceCandidate> ScoreRewards(PlayerStats player,
            IReadOnlyList<CardConfig> deck, IReadOnlyList<LevelUpSystem.Reward> rewards)
        {
            float before = BuildValue(player, deck);
            var scores = new List<RunAcceptanceCandidate>();
            for (int i = 0; i < rewards.Count; i++)
            {
                LevelUpSystem.Reward reward = rewards[i];
                if (reward == null) continue;
                float value = reward.type == LevelUpSystem.RewardType.NewCard && reward.card != null
                    ? BuildValue(player, deck.Concat(new[] { reward.card }).ToList()) - before
                    : reward.type == LevelUpSystem.RewardType.StatBoost
                        ? StandardCardHeuristics.StatGrowthValue(reward.statType, reward.statAmount) * 0.7f
                        : -1000f;
                scores.Add(new RunAcceptanceCandidate { id = i.ToString(), instanceId = i,
                    contentId = reward.card?.contentId ?? reward.statType.ToString(), score = value,
                    reason = "change-in-deck-quality" });
            }
            if (scores.Count == 0) throw new InvalidOperationException("Reward offers contain no selectable candidate.");
            return scores;
        }

        internal float BuildValue(PlayerStats player, IReadOnlyList<CardConfig> cards)
        {
            var deck = cards.Where(c => c != null).ToList();
            if (deck.Count == 0) return 0f;
            float healthRisk = 1f - player.currentHealth / (float)Math.Max(1, player.maxHealth);
            float value = 0f, draw = 0f;
            int links = 0;
            var colors = new HashSet<CardColor>();
            foreach (CardConfig card in deck)
            {
                StandardCardProfile profile = StandardCardHeuristics.Describe(card);
                int cost = card.ResolveCost().PaymentCost;
                float usable = 1f / (1f + Math.Max(0, cost - player.faLi) * 2f);
                float cardValue = DamageValue(player, card) / 8f + profile.Mana * 2.5f +
                    profile.Draw * 3f + profile.Growth * 0.35f +
                    profile.Armor * (0.8f + healthRisk * 3f) +
                    profile.Healing * (1f + healthRisk * 4f);
                if (card.destroyOnUse) cardValue *= 0.6f;
                value += cardValue * usable;
                draw += profile.Draw;
                colors.Add(card.color);
                if (deck.Any(other => !ReferenceEquals(card, other) &&
                        Math.Abs(other.ResolveCost().ComboValue - card.ResolveCost().ComboValue) == 1)) links++;
            }
            float quality = value / deck.Count;
            float playableDraw = Math.Min(player.shouPai + 2f, player.shouPai + draw / deck.Count * 2f);
            float total = quality * playableDraw * 4f + links / (float)deck.Count * 8f + colors.Count * 1.2f;
            foreach (CrawlerConfig hero in _heroes)
            {
                if (hero == null || !deck.Any(c => c.effects != null && c.effects.Any(e =>
                        e != null && e.effectType == EffectType.SummonCrawler && e.crawlerContentId == hero.contentId)))
                    continue;
                float triggerShare = deck.Count(c => c.color == hero.triggerColor) / (float)deck.Count;
                float growth = hero.triggerEffects?.Where(e => e != null && e.effectType == EffectType.ModifyStat)
                    .Sum(e => StandardCardHeuristics.StatGrowthValue(e.statType, e.amount)) ?? 0f;
                total += triggerShare * Math.Min(hero.duration, player.shouPai) * (2f + growth);
            }
            return total;
        }

        internal float DamageValue(PlayerStats player, CardConfig card)
            => AttackPlanBuilder.TryBuildFromCard(card, _rules,
                    new CombatStatSnapshot(player, 0), out AttackPlan plan)
                ? plan.SubHits.Sum(hit => hit.NormalHitDamage) : 0f;

        internal float SealValue(PlayerStats player, IReadOnlyList<CardConfig> deck,
            CardConfig host, CardSealSpec seal)
        {
            CardConfig projected = CardSealRules.BuildAssignedCard(
                host, seal, _rules, CardTestStatus.CanUseEvolutionResult);
            if (projected == null) return float.NegativeInfinity;
            try
            {
                var changed = deck.Select(c => c.runtimeInstanceId == host.runtimeInstanceId ? projected : c).ToList();
                float extra = BuildValue(player, changed) - BuildValue(player, deck);
                foreach (CardEffectSpec effect in CardSealRules.GetPostPlayEffects(new[] { seal }))
                    extra += effect.effectType == EffectType.DrawCards ? effect.count * 6f :
                        effect.effectType == EffectType.GainArmor ? effect.amount * 3f :
                        effect.effectType == EffectType.ModifyStat
                            ? StandardCardHeuristics.StatGrowthValue(effect.statType, effect.amount) * 2f : 0f;
                return extra;
            }
            finally { UnityEngine.Object.DestroyImmediate(projected); }
        }

        internal float ConversionValue(PlayerStats player, IReadOnlyList<CardConfig> deck,
            int instanceId, RunCardConversionOutcome outcome, int amount)
        {
            var remaining = deck.Where(c => c.runtimeInstanceId != instanceId).ToList();
            float loss = BuildValue(player, remaining) - BuildValue(player, deck);
            float risk = 1f - player.currentHealth / (float)Math.Max(1, player.maxHealth);
            float gain = outcome == RunCardConversionOutcome.BaseArmorPerTurn
                ? amount * (6f + risk * 16f)
                : outcome == RunCardConversionOutcome.Might
                    ? remaining.Sum(c => DamageValue(player, c)) / Math.Max(1, remaining.Count) * amount * 0.09f : 0f;
            return gain + loss;
        }

        internal float CashOutValue(int baseJade)
            => baseJade / (_objective == RunAcceptanceObjective.Exploration ? 30f : 150f);

        internal RunAcceptanceCandidate ChooseNode(PlayerStats player,
            IReadOnlyList<CardConfig> deck, IReadOnlyList<RunAcceptanceNodeOption> options)
            => ScoreNodes(player, deck, options).OrderByDescending(c => c.score)
                .ThenBy(c => c.id, StringComparer.Ordinal).FirstOrDefault();

        internal List<RunAcceptanceCandidate> ScoreNodes(PlayerStats player,
            IReadOnlyList<CardConfig> deck, IReadOnlyList<RunAcceptanceNodeOption> options)
        {
            float health = player.currentHealth / (float)Math.Max(1, player.maxHealth);
            float readiness = Math.Max(0f, Math.Min(1f, BuildValue(player, deck) / 150f));
            bool hasExplorationWork = _objective == RunAcceptanceObjective.Exploration &&
                options.Any(option => option != null && option.type != LevelNodeType.Exit && !option.boss);
            var scores = new List<RunAcceptanceCandidate>();
            foreach (RunAcceptanceNodeOption option in options)
            {
                float score;
                string reason;
                if (hasExplorationWork && (option.type == LevelNodeType.Exit || option.boss))
                {
                    // Exploration represents a human full-clear pass. Map prerequisites
                    // describe availability, but an early-open boss/exit must not skip
                    // the encounters and rewards that establish later-floor progression.
                    score = -100000f;
                    reason = "defer-terminal-until-floor-work-completes";
                }
                else if (option.type == LevelNodeType.Exit) { score = 150f; reason = "advance-floor"; }
                else if (option.type == LevelNodeType.Interaction)
                {
                    score = 10f;
                    foreach (LevelInteractionEffect effect in option.effects)
                        score += effect.kind == LevelInteractionEffectKind.HealCurrentHealth
                            ? Math.Min(Math.Max(0, player.maxHealth - player.currentHealth), effect.amount) * 8f - 15f
                            : effect.kind == LevelInteractionEffectKind.GrantJade
                                ? (_objective == RunAcceptanceObjective.Exploration ? 60f : 15f)
                                : 90f;
                    reason = "current-recovery-and-build-benefit";
                }
                else
                {
                    score = option.boss ? 35f + readiness * 35f - (1f - health) * 15f
                        : 60f - readiness * 30f - (1f - health) * 50f - option.observedHealthLoss * 2f;
                    if (!option.boss && _objective == RunAcceptanceObjective.Exploration) score += 25f;
                    if (option.guaranteedBuildReward) score += 15f;
                    reason = option.boss ? "progress-versus-readiness" : "growth-versus-observed-attrition";
                }
                scores.Add(new RunAcceptanceCandidate { id = option.id, score = score, reason = reason });
            }
            return scores;
        }
    }

    /// <summary>Visible legal map alternatives supplied by the Run adapter; no graph or hidden enemies.</summary>
    internal sealed class RunAcceptanceNodeOption
    {
        internal string id;
        internal LevelNodeType type;
        internal bool boss, guaranteedBuildReward;
        internal float observedHealthLoss;
        internal IReadOnlyList<LevelInteractionEffect> effects = Array.Empty<LevelInteractionEffect>();
    }
}
