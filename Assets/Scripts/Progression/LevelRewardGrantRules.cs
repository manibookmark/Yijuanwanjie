using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Validates and commits permanent level rewards before presentation. The
    /// caller supplies the account and persistence; there is no ambient save or
    /// UI dependency. Validation precedes all account mutations.
    /// </summary>
    public sealed class LevelRewardGrantRules
    {
        private readonly MetaProgression _account;
        private readonly Action<MetaProgression> _save;
        private readonly Func<string, string> _crawlerName;

        public LevelRewardGrantRules(MetaProgression account, Action<MetaProgression> save = null,
            Func<string, string> crawlerName = null)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _save = save ?? (_ => { });
            _crawlerName = crawlerName ?? ResolveCrawlerName;
        }

        public IReadOnlyList<LevelRewardReceipt> Preview(IReadOnlyList<LevelRewardGrant> grants)
        {
            var receipts = new List<LevelRewardReceipt>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LevelRewardGrant grant in grants ?? Array.Empty<LevelRewardGrant>())
            {
                if (grant == null || string.IsNullOrWhiteSpace(grant.contentId) ||
                    string.IsNullOrWhiteSpace(grant.targetContentId) || !ids.Add(grant.contentId))
                    throw new InvalidOperationException("Level rewards require distinct stable grant and target IDs.");
                if (grant.kind == LevelRewardKind.CrawlerUnlock)
                {
                    string name = _crawlerName(grant.targetContentId);
                    if (name == null) throw new InvalidOperationException("Reward crawler does not exist: " + grant.targetContentId);
                    receipts.Add(new LevelRewardReceipt(grant.contentId, grant.kind, grant.targetContentId,
                        name, !_account.IsCrawlerUnlocked(grant.targetContentId) && newTargets.Add(grant.targetContentId),
                        grant.presentationId));
                }
                else if (grant.kind == LevelRewardKind.RelicUnlock)
                {
                    if (!ContentCatalog.Current.TryGetRelic(grant.targetContentId, out RelicData relic) ||
                        !Enum.IsDefined(typeof(RelicType), relic.relicType))
                        throw new InvalidOperationException("Reward relic does not exist: " + grant.targetContentId);
                    receipts.Add(new LevelRewardReceipt(grant.contentId, grant.kind, grant.targetContentId,
                        relic.name, !_account.IsRelicUnlocked(grant.targetContentId) && newTargets.Add(grant.targetContentId),
                        grant.presentationId));
                }
                else throw new InvalidOperationException("Unsupported automatic level reward: " + grant.kind);
            }
            return receipts.AsReadOnly();
        }

        public IReadOnlyList<LevelRewardReceipt> Apply(IReadOnlyList<LevelRewardGrant> grants)
        {
            IReadOnlyList<LevelRewardReceipt> receipts = Preview(grants);
            if (receipts.Count == 0) return receipts;
            var before = new MetaCompletionAccountSnapshot(_account);
            try
            {
                foreach (LevelRewardReceipt receipt in receipts)
                {
                    if (receipt.Kind == LevelRewardKind.CrawlerUnlock)
                        _account.TryUnlockCrawler(receipt.TargetContentId);
                    else if (receipt.Kind == LevelRewardKind.RelicUnlock)
                    {
                        if (!ContentCatalog.Current.TryGetRelic(receipt.TargetContentId, out RelicData relic))
                            throw new InvalidOperationException("Reward relic does not exist: " + receipt.TargetContentId);
                        _account.TryUnlockRelic(relic.contentId, (RelicType)relic.relicType, relic.name, relic.description);
                    }
                }
                _save(_account);
                return receipts;
            }
            catch { before.Restore(_account); throw; }
        }

        public bool RecordLevelCleared(string levelContentId)
        {
            if (string.IsNullOrWhiteSpace(levelContentId)) return false;
            var before = new MetaCompletionAccountSnapshot(_account);
            try
            {
                bool changed = _account.TryRecordClearedLevel(levelContentId);
                if (changed) _save(_account);
                return changed;
            }
            catch { before.Restore(_account); throw; }
        }

        public static IReadOnlyList<LevelRewardGrant> GetAutomaticGrants(LevelNode node)
        {
            var result = new List<LevelRewardGrant>();
            foreach (LevelRewardGrant grant in node?.victoryRewards ?? new List<LevelRewardGrant>())
            {
                if (grant == null) throw new InvalidOperationException("A victory reward is null.");
                if (grant.kind != LevelRewardKind.SealChoice) result.Add(grant);
            }
            return result.AsReadOnly();
        }

        private static string ResolveCrawlerName(string id)
            => ContentCatalog.Current.TryGetCrawler(id, out CrawlerData crawler) ? crawler.name ?? id : null;
    }

    /// <summary>Only fields written by completion rules participate in this synchronous rollback.</summary>
    internal sealed class MetaCompletionAccountSnapshot
    {
        private readonly List<string> _crawlers, _relics, _cleared;
        private readonly List<RelicType> _relicTypes;
        private readonly bool _shop;
        private readonly int _jade, _totalJade;

        internal MetaCompletionAccountSnapshot(MetaProgression account)
        {
            _crawlers = account.unlockedCrawlers == null ? null : new List<string>(account.unlockedCrawlers);
            _relics = account.unlockedRelics == null ? null : new List<string>(account.unlockedRelics);
            _relicTypes = account.unlockedRelicTypes == null ? null : new List<RelicType>(account.unlockedRelicTypes);
            _cleared = account.clearedLevelContentIds == null ? null : new List<string>(account.clearedLevelContentIds);
            _shop = account.shopUnlocked;
            _jade = account.lingyu;
            _totalJade = account.totalLingyuEarned;
        }

        internal void Restore(MetaProgression account)
        {
            account.unlockedCrawlers = _crawlers;
            account.unlockedRelics = _relics;
            account.unlockedRelicTypes = _relicTypes;
            account.clearedLevelContentIds = _cleared;
            account.shopUnlocked = _shop;
            account.lingyu = _jade;
            account.totalLingyuEarned = _totalJade;
        }
    }
}
