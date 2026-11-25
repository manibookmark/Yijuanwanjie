using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Meta;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Cards
{
    /// <summary>
    /// 卡牌文字渲染服务：统一处理费用、描述、连击预览和 Tooltip 的文本生成。
    /// CardView 和 CardTooltipView 都通过此服务获取显示文本，
    /// 使用既有机制的新卡只需填JSON；新增机制的标签和解释在共享机制投影登记。
    ///
    /// 模块审查：本轮从原大型渲染器提取特殊机制解释，仍维护“把权威卡牌配置、
    /// Core即时摘要及AttackPlan组装为卡面/Tooltip文本”这一知识边界，不保存交互、布局或战斗
    /// 状态。CardMechanicTextProjection唯一拥有特殊机制识别及解释，卡面标签与
    /// Tooltip消费同一投影；本类只组装数值、属性和英灵摘要，不复制机制判断。
    /// 卡面文字语法与强调色由 CardFaceTextStyle 单独拥有；这里不复制逐卡颜色。
    /// 属性名称与词义共用 PowerUpGlyphConfig，不复制 HUD 的关键词说明。
    /// 若后续加入文本排版生命周期或第二种独立规则解释器，再提取专用格式化模块。
    /// </summary>
    public static class CardTextRenderer
    {
        // ── 颜色常量 ──
        private static readonly Color LabelColor = new Color(0.95f, 0.80f, 0.45f);

        // ════════════════════════════════════════════════════════════
        // 费用显示
        // ════════════════════════════════════════════════════════════

        /// <summary>获取费用显示文本。Wild→"☯"，X→"X"，Mild→"—"，否则显示数字。</summary>
        public static string GetCostText(CostType costType, int cost)
        {
            return costType switch
            {
                CostType.Wild => "☯",
                CostType.X => "X",
                CostType.Mild => "—",
                _ => cost.ToString()
            };
        }

        // ════════════════════════════════════════════════════════════
        // 卡面描述（含锋锐加成）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 战斗手牌显示本次即时实扣总量，与同份 Core 摘要的怪物预告一致。
        /// 奖励、收藏等没有战场上下文的卡面保留基础攻击能力。
        /// </summary>
        public static string GetDisplayDescription(CardDisplayData data)
        {
            string desc = CardFaceTextStyle.PrepareAuthorText(data.description);
            // 英灵卡面保留入场和驻场收益；触发时机、凝神消耗等由详情解释。
            // 只压缩具有完整驻场详情的结构化召唤牌，不按卡名分支或截断普通牌。
            if (data.effects != null && data.effects.Exists(effect =>
                    effect != null && effect.effectType == EffectType.SummonCrawler) &&
                !string.IsNullOrEmpty(data.detailDescription) &&
                data.detailDescription.Contains("驻场："))
            {
                if (!string.IsNullOrEmpty(data.name))
                    desc = desc.Replace("召唤" + data.name, "召唤");
                desc = System.Text.RegularExpressions.Regex.Replace(
                    desc, "[（(]凝神[0-9]+[）)]", "");
                desc = desc.Replace("驻场：", "").Replace("牌使", "牌：")
                    .Replace("牌结算后", "牌：").Replace("·入场", "·");
            }

            // The detached summary already includes this context's scaling.
            // Never multiply it again when hovering a hand card.
            desc = ApplyNonDamageCombo(desc, data, data.displayCombo, false);
            CardImmediateDamageDisplaySummary immediate = data.immediateDamageDisplay;
            if (immediate != null && immediate.HasDamage)
            {
                bool pending = immediate.IsEstimate && immediate.DamagePreviews.Count == 0;
                string total = (immediate.IsEstimate ? "≈" : string.Empty) + immediate.TotalDamage;
                desc = CardFaceTextStyle.WithImmediateDamage(desc, total, pending);
            }
            else if (immediate == null)
            {
                int currentTotal = data.attackDisplay?.MainDamage ??
                    GetCompatibilityDisplayedDamage(data, 0);
                if (currentTotal > 0 || data.attackDisplay?.Plans.Count > 0)
                    desc = CardFaceTextStyle.WithDamageTotal(desc, currentTotal,
                        data.attackDisplay?.IsEstimate == true);
            }

            // Layout-only samples may intentionally carry text without a card
            // definition. Real card snapshots always have the Core summary.
            if (data.attackDisplay != null)
                desc = CardMechanicTextProjection.ReconcileFace(desc,
                    CardMechanicTextProjection.Collect(data));
            return CardFaceTextStyle.HighlightKeywords(desc);
        }

        /// <summary>
        /// Compatibility for manually constructed layout/test data without a
        /// CardConfig projection. Combat faces consume immediateDamageDisplay;
        /// noncombat faces consume the base attackDisplay.
        /// All damage arithmetic still belongs to Core's AttackPlanBuilder.
        /// </summary>
        private static int GetCompatibilityDisplayedDamage(CardDisplayData data, int combo)
        {
            int total = 0;
            if (data.effects != null && data.effects.Count > 0)
            {
                foreach (CardEffectSpec effect in data.effects)
                {
                    if (effect == null || effect.effectType != EffectType.Damage)
                        continue;
                    DamageSpec damageSpec = effect.damageSpec != null &&
                        effect.damageSpec.baseHitDamage > 0
                            ? effect.damageSpec
                            : new DamageSpec
                            {
                                baseHitDamage = effect.amount,
                                pattern = new AttackPatternSpec
                                {
                                    patternType = data.attackPattern,
                                    baseAttackInstanceCount = Mathf.Max(1, effect.count)
                                }
                            };
                    if (damageSpec.baseHitDamage > 0)
                        total = checked(total + BuildCompatibilityDamage(data, damageSpec, combo));
                }
                return total;
            }
            if (data.damage <= 0) return 0;
            return BuildCompatibilityDamage(data, new DamageSpec
                {
                    baseHitDamage = data.damage,
                    pattern = new AttackPatternSpec
                        {
                            patternType = data.attackPattern,
                            baseAttackInstanceCount = Mathf.Max(1, data.projectileCount)
                        }
                }, combo);
        }

        private static int BuildCompatibilityDamage(
            CardDisplayData data, DamageSpec damageSpec, int combo)
        {
            damageSpec = CardSealRules.ApplyDamageMultiplier(damageSpec, data.seals);
            damageSpec = CardSealRules.ApplyDamageMultiplier(damageSpec,
                Mathf.Max(1f, data.previewDamageMultiplier));
            return GetDisplayedCardDamage(damageSpec, data.might, data.amount, combo,
                data.limitBreakCount, data.baseManaCost, data.manaBeforePayment);
        }

        private static int GetDisplayedCardDamage(
            DamageSpec damageSpec, int might, int amount, int combo,
            int limitBreakCount = 0, int baseManaCost = 0,
            long manaBeforePayment = 0)
        {
            var stats = new PlayerStats
            {
                fengRui = Mathf.Max(0, might),
                amount = Mathf.Max(0, amount)
            };
            return AttackPlanBuilder.Build(
                damageSpec,
                new CombatStatSnapshot(stats, combo,
                    manaBeforePayment: manaBeforePayment),
                new CardAttackSnapshot(0, Math.Max(0, limitBreakCount), baseManaCost))
                .DisplayedCardDamage;
        }

        // ════════════════════════════════════════════════════════════
        // 连击预览
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 生成连击预览后的描述文本。
        /// 在含锋锐加成的基础上再叠加连击倍率。
        /// combo ≤ 0 时返回含锋锐的卡面描述。
        /// </summary>
        public static string GetComboPreviewDescription(CardDisplayData data, int combo)
        {
            string baseDesc = GetDisplayDescription(data);
            if (combo <= 0 || data.usesCurrentPlayDisplay) return baseDesc;

            string desc = baseDesc;
            string hex = CardFaceTextStyle.HighlightHex;

            // Legacy callers can request a combo for base display data. Hand
            // snapshots above already contain the Core play projection.
            int currentTotal = data.attackDisplay?.MainDamage ??
                GetCompatibilityDisplayedDamage(data, 0);
            int comboTotal = GetCompatibilityDisplayedDamage(data, combo);
            if (comboTotal > 0 && comboTotal != currentTotal)
            {
                desc = CardFaceTextStyle.WithDamageTotal(desc,
                    $"<b><color=#{hex}>" +
                    (data.attackDisplay?.IsEstimate == true ? "≈" : string.Empty) +
                    $"{comboTotal}</color></b>");
            }

            // 非伤害效果（护甲/治疗/灵力）：不受锋锐影响，直接 ×(1+combo)
            return ApplyNonDamageCombo(desc, data, combo, true);
        }

        /// <summary>
        /// 收集非伤害类可连击缩放数值（护甲/治疗/灵力等）。
        /// </summary>
        private static List<(int baseValue, string label)> CollectNonDamageScalables(CardDisplayData data)
        {
            var result = new List<(int, string)>();
            if (data.effects == null || data.effects.Count == 0)
            {
                if (data.armor > 0) result.Add((data.armor, "armor"));
                if (data.heal > 0) result.Add((data.heal, "heal"));
                if (data.manaGain > 0) result.Add((data.manaGain, "mana"));
                return result;
            }
            var seen = new HashSet<string>();
            foreach (var spec in data.effects)
            {
                if (spec == null || !spec.comboScalable) continue;
                string label = spec.effectType switch
                {
                    EffectType.GainArmor => "armor",
                    EffectType.Heal => "heal",
                    EffectType.GainMana => "mana",
                    EffectType.DrawCards => "draw",
                    EffectType.IncreaseMaxHealth => "maxHealth",
                    EffectType.ModifyStat => "stat:" + PowerUpGlyphConfig.GetStatDisplayName(spec.statType),
                    _ => null
                };
                int baseValue = spec.effectType == EffectType.DrawCards ? spec.count : spec.amount;
                if (baseValue > 0 && label != null && seen.Add(label + ":" + baseValue))
                    result.Add((baseValue, label));
            }
            return result;
        }

        private static string ApplyNonDamageCombo(
            string text, CardDisplayData data, int combo, bool highlight)
        {
            if (combo <= 0) return text;
            foreach (var scalable in CollectNonDamageScalables(data))
            {
                int scaled = checked(scalable.baseValue * (1 + combo));
                string value = highlight
                    ? $"<b><color=#{CardFaceTextStyle.HighlightHex}>{scaled}</color></b>"
                    : scaled.ToString();
                text = CardFaceTextStyle.WithEffectValue(text, scalable.baseValue, value, scalable.label);
            }
            return text;
        }

        // ════════════════════════════════════════════════════════════
        // Tooltip 文本
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 从实际运行配置生成 Tooltip 关键词解释。
        /// 卡面保留总量，悬停说明攻击次数、每击威力及必要的多目标结构。
        /// </summary>
        public static string GetTooltipText(CardConfig card, CombatController combat = null,
            CardImmediateDamageDisplaySummary immediateDamageDisplay = null)
        {
            if (card == null) return null;
            var lines = new List<string>();
            var shown = new HashSet<string>();
            string labelHex = ColorUtility.ToHtmlStringRGB(LabelColor);

            CardAttackDisplaySummary attackDisplay = combat != null
                ? combat.GetCardAttackDisplay(card)
                : CardAttackDisplayProjection.Project(card, new CombatStatSnapshot(null, 0));
            CardImmediateDamageDisplaySummary immediate = immediateDamageDisplay ??
                combat?.GetCardImmediateDamageDisplay(card);
            string attackText = CardAttackTextFormatter.Format(attackDisplay);
            if (!string.IsNullOrEmpty(attackText))
                AddUnique(lines, shown, "damage",
                    $"<color=#{labelHex}>伤害</color>：{attackText}");
            else if (immediate != null && immediate.HasDamage)
            {
                bool pending = immediate.IsEstimate && immediate.DamagePreviews.Count == 0;
                string damageText = pending ? "待结算" :
                    (immediate.IsEstimate ? "≈" : string.Empty) + immediate.TotalDamage;
                AddUnique(lines, shown, "damage",
                    $"<color=#{labelHex}>伤害</color>：{damageText}");
            }
            CardDisplayData mechanicsData = CardDisplayData.FromCard(card);
            mechanicsData.attackDisplay = attackDisplay;
            if (mechanicsData.hasConfusedCostChange)
                AddUnique(lines, shown, "cost-confused-this-turn",
                    $"<color=#{labelHex}>扰灵</color>：本回合费用已重算，下回合恢复");
            foreach (CardMechanicText mechanic in CardMechanicTextProjection.Collect(mechanicsData))
                AddUnique(lines, shown, mechanic.Key,
                    $"<color=#{labelHex}>{mechanic.Keyword}</color>：{mechanic.Description}");
            AppendStructuredEffects(lines, shown, card, labelHex);
            AppendLegacyValues(lines, shown, card, labelHex);
            AppendCardFaceKeywordDefinitions(lines, shown, card, labelHex);
            AppendSeals(lines, shown, card, labelHex);

            if (card.ResolveCost().IsWild)
                AddUnique(lines, shown, "taiji",
                    "<color=#D9B45B>太极</color>：无需灵力，承接连击；下一张不限费用。");

            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }

        // ── Tooltip 内部方法 ──

        private static void AppendStructuredEffects(
            List<string> lines, HashSet<string> shown,
            CardConfig card, string labelHex)
        {
            if (card.effects == null) return;

            CardEffectSpec summonedCrawler = null;
            for (int i = 0; i < card.effects.Count; i++)
            {
                CardEffectSpec candidate = card.effects[i];
                if (candidate != null &&
                    candidate.effectType == EffectType.SummonCrawler)
                {
                    summonedCrawler = candidate;
                    break;
                }
            }
            if (summonedCrawler != null)
            {
                AppendCrawlerSummary(
                    lines,
                    shown,
                    card,
                    summonedCrawler,
                    labelHex);
                return;
            }

            foreach (var effect in card.effects)
            {
                if (effect == null) continue;
                switch (effect.effectType)
                {
                    case EffectType.GainArmor:
                        AddUnique(lines, shown, "armor",
                            $"<color=#{labelHex}>护甲</color>：{PowerUpGlyphConfig.GetStatDescription(StatType.HuJia)}");
                        break;
                    case EffectType.Heal:
                        AddUnique(lines, shown, "heal",
                            $"<color=#{labelHex}>治疗</color>：恢复气血，不超过气血上限");
                        break;
                    case EffectType.IncreaseMaxHealth:
                        AddUnique(lines, shown, "max-health",
                            $"<color=#{labelHex}>气血上限</color>：提高本局可拥有的最大气血");
                        break;
                    case EffectType.GainMana:
                        AddUnique(lines, shown, "mana",
                            $"<color=#{labelHex}>灵力</color>：{PowerUpGlyphConfig.GetStatDescription(StatType.FaLi)}");
                        break;
                    case EffectType.DrawCards:
                        AddUnique(lines, shown, "draw",
                            $"<color=#{labelHex}>抽牌</color>：从牌库补充手牌");
                        break;
                    case EffectType.ModifyStat:
                    {
                        AddUnique(lines, shown, $"stat:{effect.statType}",
                            $"<color=#{labelHex}>{PowerUpGlyphConfig.GetStatDisplayName(effect.statType)}</color>：" +
                            PowerUpGlyphConfig.GetStatDescription(effect.statType));
                        break;
                    }
                    case EffectType.IncreaseCrawlerDuration:
                        AddUnique(lines, shown,
                            $"stat:{StatType.NingShen}",
                            $"<color=#{labelHex}>凝神</color>：增加指定在场英灵可触发的次数");
                        break;
                    case EffectType.ExtendCrawlerDuration:
                        AddUnique(lines, shown, $"stat:{StatType.NingShen}",
                            $"<color=#{labelHex}>凝神</color>：{PowerUpGlyphConfig.GetStatDescription(StatType.NingShen)}");
                        break;

                }
            }
        }

        private static void AppendCrawlerSummary(
            List<string> lines,
            HashSet<string> shown,
            CardConfig card,
            CardEffectSpec summon,
            string labelHex)
        {
            string resident = "按其触发色持续生效";
            if (!string.IsNullOrWhiteSpace(card.detailDescription))
            {
                string[] details = card.detailDescription
                    .Replace("\r\n", "\n")
                    .Split('\n');
                for (int i = 0; i < details.Length; i++)
                {
                    string line = details[i].Trim();
                    if (TryReadLabeledDetail(
                        line,
                        "驻场",
                        out string residentDetail))
                    {
                        resident = residentDetail;
                    }
                }
            }

            resident = resident.Replace("，不受叠势加成", string.Empty)
                .Replace("，受叠势加成", string.Empty)
                .Replace("，可受引卷加成", string.Empty);
            string key = summon.crawlerContentId ?? string.Empty;
            AddUnique(lines, shown, $"crawler-summon:{key}:play",
                $"<color=#{labelHex}>召唤</color>：英灵入场后按驻场条件触发效果");
            AddUnique(lines, shown, $"crawler-summon:{key}:resident",
                $"<color=#{labelHex}>驻场</color>：{resident}");
        }

        private static bool TryReadLabeledDetail(
            string line,
            string label,
            out string detail)
        {
            detail = null;
            if (string.IsNullOrWhiteSpace(line))
                return false;
            string fullWidthPrefix = label + "：";
            string asciiPrefix = label + ":";
            int prefixLength;
            if (line.StartsWith(
                    fullWidthPrefix,
                    System.StringComparison.Ordinal))
            {
                prefixLength = fullWidthPrefix.Length;
            }
            else if (line.StartsWith(
                asciiPrefix,
                System.StringComparison.Ordinal))
            {
                prefixLength = asciiPrefix.Length;
            }
            else
            {
                return false;
            }

            detail = line.Substring(prefixLength).Trim();
            return !string.IsNullOrWhiteSpace(detail);
        }

        private static void AppendLegacyValues(
            List<string> lines, HashSet<string> shown,
            CardConfig card, string labelHex)
        {
            if (card.HasSpecEffects) return;
            if (card.armor > 0)
                AddUnique(lines, shown, "armor",
                    $"<color=#{labelHex}>护甲</color>：{PowerUpGlyphConfig.GetStatDescription(StatType.HuJia)}");
            if (card.heal > 0)
                AddUnique(lines, shown, "heal",
                    $"<color=#{labelHex}>治疗</color>：恢复气血，不超过气血上限");
            if (card.manaGain > 0)
                AddUnique(lines, shown, "mana",
                    $"<color=#{labelHex}>灵力</color>：{PowerUpGlyphConfig.GetStatDescription(StatType.FaLi)}");
        }

        // 旧卡可只在卡面写属性词。这里仅解释词义，不由文字推断任何数值或效果。
        private static void AppendCardFaceKeywordDefinitions(
            List<string> lines, HashSet<string> shown, CardConfig card, string labelHex)
        {
            string description = card.description ?? string.Empty;
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
            {
                if (stat == StatType.None) continue;
                string name = PowerUpGlyphConfig.GetStatDisplayName(stat);
                if (!description.Contains(name)) continue;
                string key = stat == StatType.FaLi ? "mana" :
                    stat == StatType.HuJia ? "armor" : $"stat:{stat}";
                AddUnique(lines, shown, key,
                    $"<color=#{labelHex}>{name}</color>：{PowerUpGlyphConfig.GetStatDescription(stat)}");
            }
            if (description.Contains("手牌+") || description.Contains("手牌数量"))
                AddUnique(lines, shown, $"stat:{StatType.ShouPai}",
                    $"<color=#{labelHex}>手牌</color>：手中持有的卡牌，数量没有上限");
            // Automatic cards describe a draw trigger, not an extra draw effect.
            // Real DrawCards specs already add their own explanation above.
            if (description.Contains("抽") && !card.autoPlayOnDraw)
                AddUnique(lines, shown, "draw",
                    $"<color=#{labelHex}>抽牌</color>：从牌库补充手牌");
            if (description.Contains("敌强"))
                AddUnique(lines, shown, "enemy-strength",
                    $"<color=#{labelHex}>敌强</color>：提高敌人强度");
            if (description.Contains("卡费"))
                AddUnique(lines, shown, "card-cost",
                    $"<color=#{labelHex}>卡费</color>：出牌时需要支付的灵力");
        }

        private static void AppendSeals(
            List<string> lines, HashSet<string> shown,
            CardConfig card, string labelHex)
        {
            if (card.seals == null) return;
            foreach (var seal in card.seals)
            {
                if (seal == null || seal.value <= 0) continue;
                string text = CardSealTooltipFormatter.Format(seal, labelHex);
                if (!string.IsNullOrWhiteSpace(text))
                    AddUnique(lines, shown, $"seal:{seal.contentId}:{seal.effect}:{seal.value}", text);
            }
        }

        private static void AddUnique(
            List<string> lines, HashSet<string> shown, string key, string line)
        {
            if (shown.Add(key)) lines.Add(line);
        }

    }
}
