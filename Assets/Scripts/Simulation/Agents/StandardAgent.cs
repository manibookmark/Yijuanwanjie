using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Agents
{
    /// <summary>
    /// 基于公开手牌、冻结意图与 Core 只读预览的短视策略。
    /// 本类只拥有估值权重，不保存或修改战斗状态，也不查看牌库顺序或消费随机数。
    /// 优先结束战斗、解除眼前致死，再比较伤害、防御及一张后续手牌的资源收益；
    /// 不搜索未来回合。平分保留手牌顺序，每次只返回一张牌，由 Runner 执行后重评。
    /// </summary>
    public sealed class StandardAgent : ISimulationAgent
    {
        public const int CurrentRevision = 2;
        public string Name => "Standard";

        public IReadOnlyList<CardConfig> ChoosePlays(CombatController combat)
        {
            if (combat == null || combat.State == null ||
                !combat.State.CanPlayCards || combat.IsCombatOver())
                return Array.Empty<CardConfig>();

            int available = combat.Mana.CurrentMana + combat.Mana.TemporaryMana;
            IReadOnlyList<EnemyIntent> intents =
                combat.State.EnemyAi?.GetPlannedIntents() ??
                Array.Empty<EnemyIntent>();
            float incoming = 0f;
            int livingEnemies = 0;
            for (int i = 0; i < combat.Enemies.Count; i++)
            {
                EnemyStats enemy = combat.Enemies[i];
                if (enemy == null || enemy.IsDead || enemy.IsWithdrawn) continue;
                livingEnemies++;
                incoming += AttackThreat(combat, intents, i);
            }

            // 费用不足的牌仍可作为回蓝后的可见机会，绝不会直接返回给 Runner。
            var candidates = new List<Candidate>(combat.Hand.Count);
            foreach (var card in combat.Hand)
            {
                if (card == null) continue;
                candidates.Add(Observe(combat, card, intents));
            }

            Candidate best = null;
            int bestPriority = -1;
            float bestScore = float.MinValue;
            foreach (Candidate candidate in candidates)
            {
                if (candidate.Cost > available) continue;
                int priority = SurvivalPriority(
                    combat, candidate, incoming, livingEnemies);
                float score = Score(combat, candidate, candidates, available, incoming);
                if (priority > bestPriority ||
                    (priority == bestPriority && score > bestScore))
                {
                    best = candidate;
                    bestPriority = priority;
                    bestScore = score;
                }
            }

            return best != null ? new[] { best.Card } : Array.Empty<CardConfig>();
        }

        private static Candidate Observe(
            CombatController combat,
            CardConfig card,
            IReadOnlyList<EnemyIntent> intents)
        {
            var candidate = new Candidate(card);
            AttackPlan plan = combat.GetAttackPlanPreview(card);
            if (plan != null)
            {
                foreach (AttackSubHit hit in plan.SubHits)
                {
                    if (hit.TargetPolicy == TargetPolicy.RandomAttackable ||
                        hit.TargetPolicy == TargetPolicy.RandomColumnBag)
                        candidate.CertainTargets = false;
                }
                // 已包括叠势、实际目标、护盾与过量伤害封顶；不再次乘叠势。
                foreach (EnemyDamagePreview preview in
                         combat.GetDeterministicDamagePreview(card))
                {
                    candidate.Damage += preview.Damage;
                    EnemyStats enemy = combat.Enemies[preview.TargetIndex];
                    if (!preview.WillKill || enemy.IsDead) continue;
                    candidate.Kills++;
                    candidate.RemovedThreat += AttackThreat(
                        combat, intents, preview.TargetIndex);
                }
            }

            ComboPreview combo = combat.GetComboPreview(card);
            candidate.ContinuesCombo = combo.WouldCombo && !combo.WouldClearCombo;
            int multiplier = combo.ResultingMultiplier;
            if (!card.HasSpecEffects)
            {
                candidate.Armor = Math.Max(0, card.armor) * multiplier;
                candidate.Healing = Math.Max(0, card.heal) * multiplier;
                candidate.Mana = Math.Max(0, card.manaGain) * multiplier;
            }
            else
            {
                foreach (CardEffectSpec effect in card.effects)
                {
                    if (effect == null) continue;
                    int scale = effect.comboScalable ? multiplier : 1;
                    float amount = Math.Max(0, effect.amount) * scale;
                    switch (effect.effectType)
                    {
                        case EffectType.GainArmor: candidate.Armor += amount; break;
                        case EffectType.Heal: candidate.Healing += amount; break;
                        case EffectType.GainMana: candidate.Mana += amount; break;
                        case EffectType.DrawCards:
                            candidate.Draw += Math.Max(0, effect.count) * scale;
                            break;
                        case EffectType.SummonCrawler:
                            if (!combat.ColorTrigger.HasActiveCrawler(effect.crawlerContentId))
                                candidate.Growth += 8f;
                            break;
                    }
                }
            }

            // Core 合并卡牌属性增量与当前英灵触发，不在策略中复制英灵颜色规则。
            foreach (BuffPreview buff in combat.GetBuffPreview(card))
                candidate.Growth += buff.delta * GrowthWeight(buff.statType);
            candidate.Healing = Math.Min(candidate.Healing,
                Math.Max(0, combat.Player.maxHealth - combat.Player.currentHealth));
            return candidate;
        }

        private static float Score(
            CombatController combat,
            Candidate candidate,
            IReadOnlyList<Candidate> hand,
            int available,
            float incoming)
        {
            float prevented = candidate.CertainTargets ? candidate.RemovedThreat : 0f;
            float usefulArmor = Math.Min(candidate.Armor,
                Math.Max(0f, incoming - prevented - combat.Player.armor));
            float score = candidate.Damage + candidate.Kills * 8f + prevented * 6f +
                usefulArmor * 5f + candidate.Healing * 4f + candidate.Growth +
                (candidate.ContinuesCombo ? 4f : 0f);

            float afterMana = available - candidate.Cost + candidate.Mana;
            float bestFollowup = 0f;
            float newlyAffordable = 0f;
            int largestOtherCost = 0;
            foreach (Candidate other in hand)
            {
                if (ReferenceEquals(other, candidate)) continue;
                largestOtherCost = Math.Max(largestOtherCost, other.Cost);
                if (other.Cost > afterMana) continue;
                bestFollowup = Math.Max(bestFollowup, other.Damage);
                if (other.Cost > available)
                    newlyAffordable = Math.Max(newlyAffordable, other.Damage);
            }
            // 只看手中一张接续牌，不执行假想出牌、不重排未知抽牌。
            if (candidate.Growth > 0f)
                score += Math.Min(80f, bestFollowup) * 0.75f;
            score += Math.Min(80f, newlyAffordable);
            score += Math.Min(candidate.Mana,
                Math.Max(0, largestOtherCost - available + candidate.Cost)) * 8f;

            float draw = candidate.Draw > 0f
                ? candidate.Draw + combat.State.Deck.ExplicitDrawBonus : 0f;
            draw = Math.Min(draw, combat.DeckCount + combat.DiscardCount);
            score += draw * (afterMana > 0f ? 12f : 6f);
            return score;
        }

        private static int SurvivalPriority(
            CombatController combat, Candidate candidate, float incoming, int livingEnemies)
        {
            // 随机目标的确定性投影只是估值，不能声称必杀或保证避开致死。
            if (candidate.CertainTargets && candidate.Kills == livingEnemies && livingEnemies > 0)
                return 2;
            float health = combat.Player.currentHealth;
            float armor = combat.Player.armor;
            if (health > Math.Max(0f, incoming - armor)) return 0;
            float remainingThreat = incoming -
                (candidate.CertainTargets ? candidate.RemovedThreat : 0f);
            return health + candidate.Healing >
                Math.Max(0f, remainingThreat - armor - candidate.Armor) ? 1 : 0;
        }

        private static float AttackThreat(
            CombatController combat, IReadOnlyList<EnemyIntent> intents, int index)
        {
            if (index < 0 || index >= intents.Count) return 0f;
            EnemyStats enemy = combat.Enemies[index];
            EnemyIntent intent = intents[index];
            return enemy == null || enemy.IsDead || enemy.IsWithdrawn ||
                intent.Type != EnemyIntentType.Attack ||
                (enemy.isDisarmed && intent.CanBeDisarmed)
                    ? 0f : Math.Max(0, intent.Amount);
        }

        private static float GrowthWeight(StatType stat) => stat switch
        {
            StatType.Amount => 12f,
            StatType.FengRui => 0.8f,
            StatType.FaLi => 12f,
            StatType.ShouPai => 12f,
            StatType.ChouPai => 10f,
            StatType.HuJia => 4f,
            StatType.NingShen => 2f,
            StatType.BengLie => 0.5f,
            StatType.MingWu => 0.4f,
            StatType.TianYun => 0.2f,
            StatType.LianYu => 0.1f,
            StatType.HunHun => 0.1f,
            StatType.ZhaoSha => -1f,
            _ => 0f
        };

        private sealed class Candidate
        {
            internal Candidate(CardConfig card)
            {
                Card = card;
                Cost = card.ResolveCost().PaymentCost;
            }

            internal readonly CardConfig Card;
            internal readonly int Cost;
            internal float Damage, RemovedThreat, Armor, Healing, Mana, Draw, Growth;
            internal int Kills;
            internal bool ContinuesCombo;
            internal bool CertainTargets = true;
        }
    }
}
