using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// 按稳定 contentId 从权威卡池解析有序卡牌列表。
    /// 必需列表在卡牌缺失或未通过准入时立即报错，可选列表只过滤未准入卡牌。
    /// </summary>
    public static class StableCardPoolResolver
    {
        public static List<CardConfig> ResolveRequired(
            IReadOnlyList<string> contentIds,
            IReadOnlyList<CardConfig> cardPool,
            Func<string, bool> isEligible,
            string errorLabel)
        {
            return Resolve(
                contentIds,
                cardPool,
                isEligible,
                errorLabel,
                rejectIneligible: true);
        }

        public static List<CardConfig> ResolveEligible(
            IReadOnlyList<string> contentIds,
            IReadOnlyList<CardConfig> cardPool,
            Func<string, bool> isEligible,
            string errorLabel)
        {
            return Resolve(
                contentIds,
                cardPool,
                isEligible,
                errorLabel,
                rejectIneligible: false);
        }

        private static List<CardConfig> Resolve(
            IReadOnlyList<string> contentIds,
            IReadOnlyList<CardConfig> cardPool,
            Func<string, bool> isEligible,
            string errorLabel,
            bool rejectIneligible)
        {
            if (contentIds == null)
                throw new ArgumentNullException(nameof(contentIds));
            if (cardPool == null)
                throw new ArgumentNullException(nameof(cardPool));

            var byId = new Dictionary<string, CardConfig>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cardPool.Count; i++)
            {
                CardConfig card = cardPool[i];
                if (card != null && !string.IsNullOrWhiteSpace(card.contentId))
                    byId[card.contentId] = card;
            }

            var result = new List<CardConfig>(contentIds.Count);
            for (int i = 0; i < contentIds.Count; i++)
            {
                string contentId = contentIds[i];
                if (!byId.TryGetValue(contentId, out CardConfig card))
                {
                    throw new InvalidOperationException(
                        $"{errorLabel}缺少卡牌：{contentId}。");
                }

                if (isEligible != null && !isEligible(contentId))
                {
                    if (rejectIneligible)
                    {
                        throw new InvalidOperationException(
                            $"{errorLabel}尚未完成验收：" +
                            $"{card.cardName}（{contentId}）。");
                    }
                    continue;
                }

                result.Add(card);
            }

            return result;
        }
    }
}
