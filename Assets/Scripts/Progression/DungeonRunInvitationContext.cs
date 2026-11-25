using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>Projects the real account's permanently unlocked summons into
    /// the project invitation policy. It owns neither the account nor a Run
    /// candidate cache; Dungeon freezes the offer and grants its one real card.</summary>
    public sealed class DungeonRunInvitationContext
    {
        private readonly MetaProgression _account;
        private readonly Func<ContentCatalog> _content;
        private readonly Func<string, bool> _isEligible;

        public DungeonRunInvitationContext(MetaProgression account, Func<ContentCatalog> content = null,
            Func<string, bool> isEligible = null)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _content = content ?? (() => ContentCatalog.Current);
            _isEligible = isEligible ?? CardTestStatus.CanUseInFormalPlay;
        }

        public bool CanAdmitCard(string contentId) => _isEligible(contentId);

        public InvitationPreparedContext Prepare(IReadOnlyList<CardConfig> authors)
        {
            if (authors == null) throw new ArgumentNullException(nameof(authors));
            var candidates = new List<InvitationCandidate>();
            foreach (var crawler in Unlocked())
            {
                CardConfig card = authors.SingleOrDefault(value => value != null && value.contentId == crawler.cardContentId);
                if (card == null)
                    throw new InvalidOperationException("已解锁英灵缺少正式召唤牌：" + crawler.contentId);
                candidates.Add(new InvitationCandidate(crawler.contentId, card, true, authors));
            }
            return new InvitationPreparedContext(candidates, _account.EventUnlocks.QueryGunForHireEligibility().IsUnlocked);
        }

        public bool HasCandidates(IReadOnlyList<string> ownedCrawlerIds)
        {
            if (ownedCrawlerIds == null) throw new ArgumentNullException(nameof(ownedCrawlerIds));
            var owned = new HashSet<string>(ownedCrawlerIds, StringComparer.Ordinal);
            return owned.Count < 3 && Unlocked().Any(value => !owned.Contains(value.contentId));
        }

        private IEnumerable<CrawlerData> Unlocked()
        {
            foreach (var value in _content().Volumes.SelectMany(volume => volume.crawlers)
                .Where(value => value != null && _account.IsCrawlerUnlocked(value.contentId))
                .OrderBy(value => value.contentId, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(value.cardContentId))
                    throw new InvalidOperationException("已解锁英灵缺少召唤牌稳定 ID：" + value.contentId);
                if (_isEligible(value.cardContentId)) yield return value;
            }
        }
    }
}
