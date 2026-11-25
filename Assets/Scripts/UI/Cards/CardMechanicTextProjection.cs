using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Cards
{
    internal readonly struct CardMechanicText
    {
        public string Key { get; }
        public string Keyword { get; }
        public string Description { get; }

        internal CardMechanicText(string key, string keyword, string description)
        {
            Key = key;
            Keyword = keyword;
            Description = description;
        }
    }

    /// <summary>
    /// Owns the vocabulary connecting short face tags to detailed mechanic text.
    /// Facts come only from detached instance/attack/effect snapshots; author text
    /// is inspected solely to preserve equivalent existing wording. No combat
    /// state, target selection, damage totals or random decisions are owned here.
    /// </summary>
    internal static class CardMechanicTextProjection
    {
        private static readonly Regex MechanicToken = new Regex(
            "^(?<keyword>自动使用|弹射|巡游|横扫|优先|随机|全体|弹跳|暴击|击退|缴械|焚阵|离火|灼烧|持续伤害|轰炸|回响|生成|手牌费用|乱魂|冰封|抹除|临时|销毁|蓄灵|灵力增伤)(?:[0-9]+[%％]?)?$",
            RegexOptions.CultureInvariant);

        internal static IReadOnlyList<CardMechanicText> Collect(CardDisplayData data)
        {
            var result = new List<CardMechanicText>();
            var shown = new HashSet<string>(StringComparer.Ordinal);
            if (data.autoPlayOnDraw)
                Add(result, shown, "automatic-draw", "自动使用", "抽到立即生效，无需点击");

            if (data.attackDisplay != null)
                foreach (AttackPlan plan in data.attackDisplay.Plans)
                    CollectAttack(result, shown, plan);

            bool destroys = data.destroyOnUse;
            if (data.effects != null)
            {
                foreach (CardEffectSpec effect in data.effects)
                {
                    if (effect == null) continue;
                    if (effect.effectType == EffectType.DestroySelf)
                        destroys = true;
                    CollectEffect(result, shown, effect, data.baseManaCost);
                }
            }

            if (data.statusEffect != StatusEffect.None &&
                data.statusEffect != StatusEffect.BaoJi &&
                data.statusEffect != StatusEffect.ZhenTui &&
                data.statusEffect != StatusEffect.JiaoXie)
                CollectStatus(result, shown, data.statusEffect, data.statusChance,
                    0, "status-legacy");

            if (data.isEncounterTemporary)
                Add(result, shown, "encounter-temporary", "临时",
                    "仅本次战斗存在，战斗结束时移除");
            if (destroys)
                Add(result, shown, "destroy", "销毁", data.autoPlayOnDraw
                    ? "自动使用后移除，不进入出牌库"
                    : data.isEncounterTemporary
                        ? "打出后移除，不进入弃牌循环" : "打出后本局移除");
            return result.AsReadOnly();
        }

        private static void CollectAttack(
            List<CardMechanicText> result, HashSet<string> shown, AttackPlan plan)
        {
            if (plan == null || plan.SubHits.Count == 0) return;
            if (plan.RicochetProjectile != null)
                Add(result, shown, "ricochet", "弹射",
                    "先攻击目标一次，再沿路线反弹；后续命中继续造成伤害");

            int attackCount = 0;
            foreach (AttackSubHit hit in plan.SubHits)
                attackCount = Math.Max(attackCount, hit.AttackInstanceIndex + 1);

            foreach (AttackSubHit hit in plan.SubHits)
            {
                switch (hit.TargetPolicy)
                {
                    case TargetPolicy.ColumnFrontOnceLeftToRight:
                        Add(result, shown, "patrol", "巡游",
                            "同次不重复命中，后续巡游可攻击新露出的敌人");
                        break;
                    case TargetPolicy.SequentialColumnSweep:
                        Add(result, shown, "sweep", "横扫",
                            "同次不重复命中，击败前敌后可继续攻击后方");
                        break;
                    case TargetPolicy.UpcomingAttackFirst:
                    case TargetPolicy.UpcomingAttackFirstRightToLeft:
                        Add(result, shown, "attack-priority", "优先",
                            "优先攻击本回合即将攻击的前敌");
                        break;
                    case TargetPolicy.RandomAttackable:
                        Add(result, shown, "random-attackable", "随机",
                            "在当前最近可攻击行中随机选择敌人");
                        break;
                    case TargetPolicy.RandomColumnBag:
                        Add(result, shown, "random-column-bag", "随机",
                            "随机选择列，攻击该列前敌；空列也会消耗本次选列");
                        break;
                    case TargetPolicy.RandomColumnBagMultiHit:
                        Add(result, shown, "random-column-hits", "随机",
                            "随机选列攻击前敌，空列不消耗命中次数，可重复命中同一敌人");
                        break;
                    case TargetPolicy.AllAttackable:
                        Add(result, shown, "all-attackable", "全体",
                            "攻击当前可攻击的全部前敌");
                        break;
                }
                CollectBounce(result, shown, hit.Bounce, attackCount);
                if (hit.Crit != null && hit.Crit.enabled)
                {
                    int chance = Percent(hit.Crit.chance);
                    int multiplier = Mathf.RoundToInt(Mathf.Max(1f, hit.Crit.multiplier) * 100f);
                    Add(result, shown, $"crit:{chance}:{multiplier}", "暴击",
                        $"{chance}%，造成{multiplier}%伤害");
                }
                foreach (ResolvedOnHitEffectSpec effect in hit.OnHitEffects)
                {
                    int chance = Percent(effect.baseChance);
                    string name = effect.effectType == OnHitEffectType.Knockback ? "击退" : "缴械";
                    Add(result, shown, $"on-hit:{effect.effectType}:{chance}", name, $"{chance}%");
                }
                CollectTerrain(result, shown, hit);
            }
        }

        private static void CollectBounce(List<CardMechanicText> result,
            HashSet<string> shown, ResolvedBounceSpec bounce, int attackCount)
        {
            if (bounce == null || !bounce.enabled) return;
            int count = Mathf.Max(0, bounce.hitsPerDirectHit);
            switch (bounce.sequenceMode)
            {
                case BounceSequenceMode.NativePerAttackInstance:
                    Add(result, shown, $"native-bounce:{count}", "弹跳",
                        $"每个攻击物最多命中{1 + count}次，不能连续命中同一敌人，可再次命中先前目标");
                    break;
                case BounceSequenceMode.SynchronizedWavesByAttackInstanceCount:
                    Add(result, shown, $"synchronized-bounce:{count}:{attackCount}", "弹跳",
                        $"至少2个目标时，首轮后追加最多{count}轮，每轮攻击{attackCount}次；每轮开始只剩1个目标则停止");
                    break;
                case BounceSequenceMode.PerDirectHit:
                    string increment = bounce.damageIncrementPerBounce == 0 ? string.Empty :
                        bounce.damageIncrementPerBounce > 0
                            ? $"；每次弹跳伤害递增{bounce.damageIncrementPerBounce}"
                            : $"；每次弹跳伤害递减{-(long)bounce.damageIncrementPerBounce}";
                    Add(result, shown, $"direct-bounce:{count}:{bounce.damageIncrementPerBounce}",
                        "弹跳", $"每次直接命中后，追加最多{count}次弹跳，同次不重复命中{increment}");
                    break;
            }
        }

        private static void CollectTerrain(List<CardMechanicText> result,
            HashSet<string> shown, AttackSubHit hit)
        {
            ResolvedTerrainEffectSpec terrain = hit.TerrainEffect;
            if (terrain == null) return;
            if (terrain.targetMode == TerrainTargetMode.ColumnDynamicFront)
            {
                string lifetime = terrain.persistUntilCombatEnd
                    ? "持续到战斗结束" : "本回合末攻击后消失";
                Add(result, shown, $"terrain:{terrain.effectId}:{terrain.baseMagnitude}:{lifetime}",
                    "焚阵", $"落符留下火阵，可叠加。每层在回合末造成{terrain.baseMagnitude}伤害，{lifetime}。");
                return;
            }
            if (string.IsNullOrWhiteSpace(terrain.effectId) || terrain.magnitudeScale <= 0f ||
                terrain.triggerCount <= 0 || terrain.triggerTiming != TerrainEffectTiming.EnemyTurnStarted)
                return;
            // Preserve the existing tooltip's conversion of this frozen terrain spec.
            int damage = DamageSystem.RoundHalfUp(hit.NormalHitDamage * terrain.magnitudeScale);
            string name = terrain.damageKind == DamageKind.Burn ? "灼烧" : "持续伤害";
            Add(result, shown, $"terrain:{terrain.effectId}:{damage}:{terrain.triggerCount}", name,
                $"命中格留下伤害区域，敌方回合开始时每层造成{damage}伤害，触发{terrain.triggerCount}次");
        }

        private static void CollectEffect(List<CardMechanicText> result,
            HashSet<string> shown, CardEffectSpec effect, int baseManaCost)
        {
            switch (effect.effectType)
            {
                case EffectType.Damage:
                    if (effect.repeatAtPlayerTurnEnd)
                    {
                        BirdAttackSpec bird = effect.damageSpec?.birdAttack;
                        Add(result, shown, "periodic-attack", "轰炸", bird != null
                            ? $"本场每次结束回合，每只驻场单位发射{bird.endTurnMissilesPerBird}发，每发基础{bird.endTurnDamagePerHit}伤害；再次召出可累积"
                            : "本场每次结束回合自动再次攻击，包括打出当回合");
                    }
                    if (effect.replayColumnsAtPlayerTurnEnd)
                        Add(result, shown, "column-replay", "回响", "回合结束，再攻击一次");
                    float coefficient = effect.damageSpec?.manaDamageCoefficient ?? 0f;
                    if (coefficient > 0f)
                    {
                        string percent = (coefficient * 100f).ToString("0.##", CultureInfo.InvariantCulture);
                        string minimum = baseManaCost > 0 ? $"，至少按{baseManaCost}点计算" : string.Empty;
                        Add(result, shown, $"mana-damage:{percent}:{baseManaCost}", "蓄灵",
                            $"灵力增伤，出牌前每点灵力使伤害提高{percent}%{minimum}");
                    }
                    break;
                case EffectType.CreateCard:
                    Add(result, shown, "create-card", "生成", "生成临时卡加入手牌，战斗结束时移除");
                    break;
                case EffectType.RandomizeHandCosts:
                    string range = effect.randomCostRelative
                        ? $"随机调整{effect.randomCostMin}～{effect.randomCostMax}点"
                        : $"随机改为{effect.randomCostMin}～{effect.randomCostMax}点";
                    Add(result, shown, "random-cost", "手牌费用", $"数字卡费用{range}");
                    break;
                case EffectType.RandomizeCrawlerTriggerColors:
                    Add(result, shown, "random-crawler-trigger", "乱魂",
                        "每名驻场英灵独立随机触发五行（土、火、水、木），可能保持原色。仅本场有效；技能与凝神不变，之后入场的英灵不受影响。");
                    break;
                case EffectType.RemoveEnemyIntent:
                    string intent = effect.attackIntentOnly ? "攻击行动" : "行动";
                    Add(result, shown, "remove-intent", "缴械",
                        $"随机至多{Mathf.Max(0, effect.count)}名敌人，各按抗性取消当前{intent}；纯等待不受影响");
                    break;
                case EffectType.ApplyStatus:
                    CollectStatus(result, shown, effect.statusId, effect.chance, effect.duration, "status-spec");
                    break;
                case EffectType.Bounce:
                    Add(result, shown, "bounce", "弹跳",
                        $"最多 {Mathf.Max(0, effect.count)} 次，每次 {Mathf.Max(0, effect.amount)} 伤害");
                    break;
                case EffectType.Burn:
                    string duration = effect.duration > 0 ? $"，持续 {effect.duration} 回合" : string.Empty;
                    Add(result, shown, "burn-spec", "灼烧",
                        $"{Percent(effect.chance)}%几率施加 {Mathf.Max(1, effect.amount)} 层{duration}");
                    break;
                case EffectType.Erase:
                    string target = effect.target == TargetRule.NearestAttackableRow
                        ? "最近可攻击行中的普通／精英与层Boss；关Boss免疫，不追击补位"
                        : "普通／精英与层Boss；关Boss免疫";
                    Add(result, shown, "erase", "抹除", $"直接击败{target}；不造成伤害");
                    break;
            }
        }

        private static void CollectStatus(List<CardMechanicText> result, HashSet<string> shown,
            StatusEffect status, float probability, int duration, string prefix)
        {
            if (status == StatusEffect.None) return;
            string name = status switch
            {
                StatusEffect.BaoJi => "暴击",
                StatusEffect.ZhenTui => "击退",
                StatusEffect.JiaoXie => "缴械",
                StatusEffect.ZhuoShao => "灼烧",
                StatusEffect.BingFeng => "冰封",
                StatusEffect.MoSha => "抹除",
                _ => status.ToString()
            };
            string lifetime = duration > 0 ? $"，持续 {duration} 回合" : string.Empty;
            Add(result, shown, $"{prefix}:{status}", name, $"{Percent(probability)}%几率{lifetime}");
        }

        /// <summary>
        /// Reconciles only owned mechanic tokens; numeric, summon and other prose
        /// stays authored. Equivalent existing phrases suppress extra short tags.
        /// Named generation and numbered tags remain intact when their mechanic exists.
        /// </summary>
        internal static string ReconcileFace(string text, IReadOnlyList<CardMechanicText> mechanics)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            if (mechanics != null)
                foreach (CardMechanicText mechanic in mechanics)
                    active.Add(mechanic.Keyword);
            var present = new HashSet<string>(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string source in (text ?? string.Empty).Split('·'))
            {
                string part = source.Trim();
                if (part.Length == 0) continue;
                RememberEquivalentPhrases(part, active, present);
                part = Regex.Replace(part, @"\S+", match =>
                {
                    string token = match.Value;
                    string keyword = ReadTokenKeyword(token);
                    if (keyword == null) return token;
                    if (!active.Contains(keyword) || !present.Add(keyword)) return string.Empty;
                    if (token == "离火") return "焚阵";
                    return token.StartsWith("灵力增伤", StringComparison.Ordinal)
                        ? "蓄灵" + token.Substring("灵力增伤".Length) : token;
                }).Trim();
                if (part.Length > 0) parts.Add(part);
            }
            if (mechanics != null)
                foreach (CardMechanicText mechanic in mechanics)
                {
                    if (!present.Add(mechanic.Keyword)) continue;
                    int order = FaceOrder(mechanic.Keyword);
                    int index = parts.FindIndex(part => PartOrder(part) > order);
                    if (index < 0) parts.Add(mechanic.Keyword);
                    else parts.Insert(index, mechanic.Keyword);
                }
            return string.Join("·", parts);
        }

        // Only newly supplied tags are positioned; existing prose keeps its order.
        private static int PartOrder(string text)
        {
            int order = -1;
            foreach (Match match in Regex.Matches(text, @"\S+"))
            {
                string keyword = ReadTokenKeyword(match.Value);
                if (keyword != null) order = Math.Max(order, FaceOrder(keyword));
            }
            return order;
        }

        private static int FaceOrder(string keyword)
        {
            switch (keyword)
            {
                case "优先": case "随机": case "全体":
                case "巡游": case "横扫": case "弹射":
                    return 0;
                case "生成": case "手牌费用": case "乱魂":
                    return 2;
                case "自动使用": case "临时":
                    return 3;
                case "销毁":
                    return 4;
                default:
                    return 1;
            }
        }

        private static string ReadTokenKeyword(string token)
        {
            Match match = MechanicToken.Match(token);
            if (match.Success)
            {
                string keyword = match.Groups["keyword"].Value;
                if (keyword == "离火") return "焚阵";
                return keyword == "灵力增伤" ? "蓄灵" : keyword;
            }
            if (token == "前排抹除") return "抹除";
            if (token.StartsWith("生成", StringComparison.Ordinal)) return "生成";
            if (token == "抽到即用" || token == "抽到自动使用" || token == "抽到后自动使用")
                return "自动使用";
            return null;
        }

        private static void RememberEquivalentPhrases(string text,
            HashSet<string> active, HashSet<string> present)
        {
            if (active.Contains("自动使用") &&
                (text.Contains("抽到：") || text.Contains("抽到:")))
                present.Add("自动使用");
            if (active.Contains("乱魂") && text.Contains("英灵") &&
                text.Contains("五行") && text.Contains("随机"))
                present.Add("乱魂");
            if (active.Contains("手牌费用") &&
                Regex.IsMatch(text, @"(?:手牌)?费用[±+＋\-－0-9]"))
                present.Add("手牌费用");
        }

        private static int Percent(float chance) => Mathf.RoundToInt(Mathf.Clamp01(chance) * 100f);

        private static void Add(List<CardMechanicText> result, HashSet<string> shown,
            string key, string keyword, string description)
        {
            if (shown.Add(key)) result.Add(new CardMechanicText(key, keyword, description));
        }
    }
}
