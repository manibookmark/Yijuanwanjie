using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// "文案存在但无行为"审计报告。
    /// 列出所有有描述文字但没有旧字段行为或结构化效果的卡牌。
    /// 阻止无效果卡牌静默进入正式内容。
    /// </summary>
    public static class CardEffectAudit
    {
        public sealed class AuditEntry
        {
            public string contentId;
            public string cardName;
            public string volumeId;
            public string description;
        }

        public sealed class AuditReport
        {
            public List<AuditEntry> TextOnlyCards { get; } = new List<AuditEntry>();
            public int TotalCards { get; set; }
            public int EffectfulCards { get; set; }

            public bool HasTextOnlyCards => TextOnlyCards.Count > 0;

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== 卡牌效果审计报告 ===");
                sb.AppendLine($"总卡牌数: {TotalCards}");
                sb.AppendLine($"有效果卡牌: {EffectfulCards}");
                sb.AppendLine($"仅文案无行为卡牌: {TextOnlyCards.Count}");

                if (HasTextOnlyCards)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- 仅文案无行为卡牌清单 ---");
                    foreach (var entry in TextOnlyCards)
                    {
                        sb.AppendLine($"  [{entry.volumeId}] {entry.cardName} ({entry.contentId})");
                        if (!string.IsNullOrEmpty(entry.description))
                            sb.AppendLine($"    描述: {entry.description}");
                    }
                }

                return sb.ToString();
            }
        }

        /// <summary>生成审计报告。</summary>
        public static AuditReport Generate(ContentCatalog catalog)
        {
            var report = new AuditReport();
            int total = 0;
            int effectful = 0;

            foreach (var volume in catalog.Volumes)
            {
                foreach (var card in volume.cards)
                {
                    total++;

                    if (HasEffect(card))
                    {
                        effectful++;
                    }
                    else
                    {
                        report.TextOnlyCards.Add(new AuditEntry
                        {
                            contentId = card.contentId,
                            cardName = card.name,
                            volumeId = card.volumeId,
                            description = card.description
                        });
                    }
                }
            }

            report.TotalCards = total;
            report.EffectfulCards = effectful;
            return report;
        }

        /// <summary>判断卡牌是否有实际游戏行为。</summary>
        public static bool HasEffect(CardData card)
        {
            if (card == null) return false;
            if (card.attack?.IsConfigured == true) return true;
            if (card.baseDamage > 0) return true;
            if (card.armor > 0) return true;
            if (card.heal > 0) return true;
            if (card.manaGain > 0) return true;
            if (!string.IsNullOrEmpty(card.statusEffect) && card.statusEffect != "None") return true;
            if (card.effects != null && card.effects.Any(effect =>
                    effect != null &&
                    effect.effectType != EffectType.None &&
                    effect.effectType != EffectType.DestroySelf))
                return true;
            return false;
        }

        /// <summary>
        /// 校验卡面明确承诺的效果类型是否存在。只解释稳定、无歧义的关键词；
        /// 具体数值继续由逐卡合同测试负责，避免让自然语言成为战斗协议。
        /// </summary>
        public static IReadOnlyList<string> GetDescriptionContractIssues(
            CardData card, Func<string, CrawlerData> resolveCrawler = null)
        {
            var issues = new List<string>();
            if (card == null) return issues;

            if (!HasEffect(card))
                issues.Add("卡牌没有任何实际游戏效果；销毁本身不能作为唯一效果。");

            string description = card.description ?? string.Empty;
            RequirePromisedEffect(
                issues,
                ContainsAny(description, "最大生命", "气血上限"),
                HasEffect(card, EffectType.IncreaseMaxHealth),
                "文案承诺提高最大生命，但未配置 IncreaseMaxHealth。");
            RequirePromisedEffect(
                issues,
                description.Contains("抽") && description.Contains("牌"),
                HasEffect(card, EffectType.DrawCards) ||
                    HasResidentEffect(card, EffectType.DrawCards, resolveCrawler),
                "文案承诺抽牌，但未配置 DrawCards。");
            RequirePromisedEffect(
                issues,
                description.Contains("灵玉"),
                HasEffect(card, EffectType.GainJade),
                "文案承诺获得灵玉，但未配置 GainJade。");
            RequirePromisedEffect(
                issues,
                description.Contains("护甲"),
                card.armor > 0 || HasEffect(card, EffectType.GainArmor),
                "文案承诺获得护甲，但未配置 GainArmor。");
            RequirePromisedEffect(
                issues,
                description.Contains("灵力"),
                card.manaGain > 0 || HasEffect(card, EffectType.GainMana),
                "文案承诺获得灵力，但未配置 GainMana。");
            RequirePromisedEffect(
                issues,
                description.Contains("天运"),
                card.effects != null && card.effects.Any(effect =>
                    effect != null &&
                    effect.effectType == EffectType.ModifyStat &&
                    effect.statType == StatType.TianYun),
                "文案承诺提高天运，但未配置对应 ModifyStat。");

            return issues;
        }

        private static bool HasEffect(CardData card, EffectType effectType)
        {
            return card.effects != null && card.effects.Any(effect =>
                effect != null && effect.effectType == effectType);
        }

        private static bool HasResidentEffect(CardData card, EffectType effectType,
            Func<string, CrawlerData> resolveCrawler)
        {
            if (resolveCrawler == null || card.effects == null) return false;
            return card.effects.Any(effect =>
                effect != null && effect.effectType == EffectType.SummonCrawler &&
                !string.IsNullOrWhiteSpace(effect.crawlerContentId) &&
                resolveCrawler(effect.crawlerContentId)?.triggerEffects?.Any(trigger =>
                    trigger != null && trigger.effectType == effectType &&
                    trigger.count > 0) == true);
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            return values.Any(value =>
                text.IndexOf(value, StringComparison.Ordinal) >= 0);
        }

        private static void RequirePromisedEffect(
            ICollection<string> issues,
            bool promised,
            bool configured,
            string message)
        {
            if (promised && !configured)
                issues.Add(message);
        }
    }
}
