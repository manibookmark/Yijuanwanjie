using System.Text.RegularExpressions;

namespace WanjieScroll.UI.Cards
{
    /// <summary>
    /// Owns card-face text syntax and emphasis. Damage totals are supplied by
    /// Core's immediate summary or base attack plan; this formatter never
    /// calculates combat rules.
    /// Author text stays plain, while all card renderers share one keyword color.
    /// </summary>
    internal static class CardFaceTextStyle
    {
        internal const string HighlightHex = "B82114";

        private static readonly Regex AuthorColorTags = new Regex(
            @"</?color(?:\s*=\s*[^>]+)?>", RegexOptions.IgnoreCase);
        private static readonly Regex TagSpacing = new Regex(@"\s*·\s*");
        private static readonly Regex DamageAmount = new Regex(
            @"(?<![0-9])(?:≈)?[0-9]+(?:\s*[×xX*]\s*[0-9]+)*(?=伤害|伤(?:[·\s（(，。；]|$))");
        private static readonly Regex Keywords = new Regex(
            "暴击|击退|弹跳|弹射|巡游|灼烧|缴械|冰封|离火|轰炸|回响|横扫|焚阵|抹除|临时|销毁|生成|留存|净化|优先|自动使用|乱魂|手牌费用|蓄灵|灵力增伤|全体|随机|指定");

        internal static string PrepareAuthorText(string text)
        {
            string plain = AuthorColorTags.Replace(text ?? string.Empty, string.Empty);
            return TagSpacing.Replace(plain, "·").Trim().TrimStart('·');
        }

        internal static string WithDamageTotal(string text, int total, bool isEstimate = false)
            => WithDamageTotal(text, (isEstimate ? "≈" : string.Empty) + total);

        internal static string WithDamageTotal(string text, string formattedTotal)
            => DamageAmount.Replace(text, _ => formattedTotal, 1);

        // A support card may gain immediate damage from a seal and therefore
        // have no authored damage token. The Core summary decides whether a
        // damage field belongs here; this method only owns its compact syntax.
        internal static string WithImmediateDamage(string text, string formattedTotal,
            bool pending)
        {
            string damageText = pending ? "伤害待结算" : formattedTotal + "伤害";
            if (DamageAmount.IsMatch(text))
            {
                // Older author text can say 前排10伤. The new value is the
                // whole play's total, so retain neither a per-row qualifier
                // nor the abbreviated unit that could suggest a per-target hit.
                var field = new Regex(@"(?:前排|全体)?" + DamageAmount + @"(?:伤害|伤)");
                return field.Replace(text, _ => damageText, 1);
            }
            return string.IsNullOrEmpty(text) ? damageText : damageText + "·" + text;
        }

        internal static string WithEffectValue(
            string text, int baseValue, string formattedValue, string label)
        {
            string words = label switch
            {
                "armor" => "护甲",
                "heal" => "气血|血",
                "mana" => "灵力",
                "draw" => "张牌|牌",
                "maxHealth" => "最大生命|气血上限|最大气血",
                _ => label.StartsWith("stat:")
                    ? Regex.Escape(label.Substring(5)) : null
            };
            if (string.IsNullOrEmpty(words)) return text;

            // Select by effect wording, so a heal amount equal to the damage
            // amount cannot replace the card's damage token.
            var suffix = new Regex($@"(?<![0-9]){baseValue}(?=(?:{words}))");
            if (suffix.IsMatch(text))
                return suffix.Replace(text, _ => formattedValue, 1);
            if (label == "draw") words = "抽";
            var prefix = new Regex(
                $@"(?<label>(?:{words})[^0-9·\r\n]{{0,2}}){baseValue}(?![0-9])");
            return prefix.Replace(text,
                match => match.Groups["label"].Value + formattedValue, 1);
        }

        internal static string HighlightKeywords(string text)
            => Keywords.Replace(text, match =>
                $"<color=#{HighlightHex}>{match.Value}</color>");
    }
}
