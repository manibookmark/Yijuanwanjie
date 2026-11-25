using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>一张稳定卡牌内容在整次 Run 中的累计表现。</summary>
    internal readonly struct DungeonRunCardStatistics
    {
        internal DungeonRunCardStatistics(
            string contentId,
            int timesPlayed,
            int damageDealt)
        {
            ContentId = contentId ?? string.Empty;
            TimesPlayed = Math.Max(0, timesPlayed);
            DamageDealt = Math.Max(0, damageDealt);
        }

        internal string ContentId { get; }
        internal int TimesPlayed { get; }
        internal int DamageDealt { get; }
    }

    /// <summary>结算冻结边界消费的整局统计快照。</summary>
    internal sealed class DungeonRunStatisticsSnapshot
    {
        internal DungeonRunStatisticsSnapshot(
            int enemiesDefeated,
            int maxCombo,
            int totalHealing,
            int freeCardsPlayed,
            int crawlerActionsTriggered,
            IReadOnlyList<DungeonRunCardStatistics> cards)
        {
            EnemiesDefeated = Math.Max(0, enemiesDefeated);
            MaxCombo = Math.Max(0, maxCombo);
            TotalHealing = Math.Max(0, totalHealing);
            FreeCardsPlayed = Math.Max(0, freeCardsPlayed);
            CrawlerActionsTriggered = Math.Max(0, crawlerActionsTriggered);
            Cards = CopyCards(cards);
        }

        internal int EnemiesDefeated { get; }
        internal int MaxCombo { get; }
        internal int TotalHealing { get; }
        internal int FreeCardsPlayed { get; }
        internal int CrawlerActionsTriggered { get; }
        internal IReadOnlyList<DungeonRunCardStatistics> Cards { get; }

        private static IReadOnlyList<DungeonRunCardStatistics> CopyCards(
            IReadOnlyList<DungeonRunCardStatistics> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<DungeonRunCardStatistics>();
            var copy = new DungeonRunCardStatistics[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i];
            return new ReadOnlyCollection<DungeonRunCardStatistics>(copy);
        }
    }

    /// <summary>
    /// 跨战斗统计的唯一所有者。每场 Combat 只提交一次不可变快照；地图恢复只提交
    /// 实际恢复量。序列化细节与逐牌聚合均不会泄漏给 Run 聚合根。
    /// </summary>
    internal sealed class DungeonRunStatistics
    {
        private sealed class MutableCardStatistics
        {
            internal MutableCardStatistics(string contentId)
            {
                ContentId = contentId;
            }

            internal string ContentId { get; }
            internal int TimesPlayed { get; set; }
            internal int DamageDealt { get; set; }
        }

        private readonly Dictionary<string, MutableCardStatistics> _cards =
            new Dictionary<string, MutableCardStatistics>(
                StringComparer.OrdinalIgnoreCase);
        private int _enemiesDefeated;
        private int _maxCombo;
        private int _totalHealing;
        private int _freeCardsPlayed;
        private int _crawlerActionsTriggered;

        internal void AddCombat(CombatStatisticsSnapshot combat)
        {
            if (combat == null)
                throw new ArgumentNullException(nameof(combat));

            _enemiesDefeated = SaturatingAdd(
                _enemiesDefeated,
                combat.EnemiesDefeated);
            _maxCombo = Math.Max(_maxCombo, combat.MaxCombo);
            _totalHealing = SaturatingAdd(
                _totalHealing,
                combat.TotalHealing);
            _freeCardsPlayed = SaturatingAdd(
                _freeCardsPlayed,
                combat.FreeCardsPlayed);
            _crawlerActionsTriggered = SaturatingAdd(
                _crawlerActionsTriggered,
                combat.CrawlerActionsTriggered);

            for (int i = 0; i < combat.Cards.Count; i++)
            {
                CombatCardStatistics source = combat.Cards[i];
                if (string.IsNullOrWhiteSpace(source.ContentId))
                    continue;
                MutableCardStatistics target = GetOrCreate(source.ContentId);
                target.TimesPlayed = SaturatingAdd(
                    target.TimesPlayed,
                    source.TimesPlayed);
                target.DamageDealt = SaturatingAdd(
                    target.DamageDealt,
                    source.DamageDealt);
            }
        }

        internal void RecordExternalHealing(int actualHealing)
        {
            if (actualHealing > 0)
                _totalHealing = SaturatingAdd(_totalHealing, actualHealing);
        }

        internal DungeonRunStatisticsSnapshot Capture()
        {
            var ids = new List<string>(_cards.Keys);
            ids.Sort(StringComparer.Ordinal);
            var cards = new List<DungeonRunCardStatistics>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                MutableCardStatistics card = _cards[ids[i]];
                cards.Add(new DungeonRunCardStatistics(
                    card.ContentId,
                    card.TimesPlayed,
                    card.DamageDealt));
            }
            return new DungeonRunStatisticsSnapshot(
                _enemiesDefeated,
                _maxCombo,
                _totalHealing,
                _freeCardsPlayed,
                _crawlerActionsTriggered,
                cards);
        }

        // Restore the existing statistics owner, including every per-card ledger.
        internal void RestoreTransactionState(DungeonRunStatisticsSnapshot previous)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            _enemiesDefeated = previous.EnemiesDefeated;
            _maxCombo = previous.MaxCombo;
            _totalHealing = previous.TotalHealing;
            _freeCardsPlayed = previous.FreeCardsPlayed;
            _crawlerActionsTriggered = previous.CrawlerActionsTriggered;
            _cards.Clear();
            foreach (DungeonRunCardStatistics card in previous.Cards)
            {
                MutableCardStatistics restored = GetOrCreate(card.ContentId);
                restored.TimesPlayed = card.TimesPlayed;
                restored.DamageDealt = card.DamageDealt;
            }
        }

        internal DungeonRunStatisticsSaveData CaptureSaveData()
        {
            DungeonRunStatisticsSnapshot snapshot = Capture();
            var data = new DungeonRunStatisticsSaveData
            {
                enemiesDefeated = snapshot.EnemiesDefeated,
                maxCombo = snapshot.MaxCombo,
                totalHealing = snapshot.TotalHealing,
                freeCardsPlayed = snapshot.FreeCardsPlayed,
                crawlerActionsTriggered = snapshot.CrawlerActionsTriggered
            };
            for (int i = 0; i < snapshot.Cards.Count; i++)
            {
                DungeonRunCardStatistics card = snapshot.Cards[i];
                data.cards.Add(new DungeonRunCardStatisticsSaveData
                {
                    contentId = card.ContentId,
                    timesPlayed = card.TimesPlayed,
                    damageDealt = card.DamageDealt
                });
            }
            return data;
        }

        internal static DungeonRunStatistics Restore(
            DungeonRunStatisticsSaveData data,
            Func<string, string> normalizeContentId = null)
        {
            var statistics = new DungeonRunStatistics();
            if (data == null)
                return statistics;
            if (data.enemiesDefeated < 0 || data.maxCombo < 0 ||
                data.totalHealing < 0 || data.freeCardsPlayed < 0 ||
                data.crawlerActionsTriggered < 0)
            {
                throw new InvalidOperationException(
                    "Run 统计包含负数总计。");
            }

            statistics._enemiesDefeated = data.enemiesDefeated;
            statistics._maxCombo = data.maxCombo;
            statistics._totalHealing = data.totalHealing;
            statistics._freeCardsPlayed = data.freeCardsPlayed;
            statistics._crawlerActionsTriggered =
                data.crawlerActionsTriggered;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (data.cards == null)
                return statistics;
            for (int i = 0; i < data.cards.Count; i++)
            {
                DungeonRunCardStatisticsSaveData source = data.cards[i] ??
                    throw new InvalidOperationException(
                        $"Run 统计第 {i} 个卡牌条目为空。");
                if (string.IsNullOrWhiteSpace(source.contentId) ||
                    !ids.Add(source.contentId.Trim()) ||
                    source.timesPlayed < 0 || source.damageDealt < 0)
                {
                    throw new InvalidOperationException(
                        $"Run 统计第 {i} 个卡牌条目无效。");
                }
            }

            // Validate the original identities before migration. Distinct retired
            // IDs may converge on one definition; malformed duplicate rows may not.
            for (int i = 0; i < data.cards.Count; i++)
            {
                DungeonRunCardStatisticsSaveData source = data.cards[i];
                string contentId = source.contentId.Trim();
                if (normalizeContentId != null)
                    contentId = normalizeContentId(contentId);
                if (string.IsNullOrWhiteSpace(contentId))
                    throw new InvalidOperationException(
                        $"Run 统计第 {i} 个卡牌条目归一后缺少内容 ID。");
                MutableCardStatistics target =
                    statistics.GetOrCreate(contentId.Trim());
                target.TimesPlayed = SaturatingAdd(target.TimesPlayed, source.timesPlayed);
                target.DamageDealt = SaturatingAdd(target.DamageDealt, source.damageDealt);
            }
            return statistics;
        }

        private MutableCardStatistics GetOrCreate(string contentId)
        {
            if (!_cards.TryGetValue(
                    contentId,
                    out MutableCardStatistics statistics))
            {
                statistics = new MutableCardStatistics(contentId);
                _cards.Add(contentId, statistics);
            }
            return statistics;
        }

        private static int SaturatingAdd(int current, int delta)
            => (int)Math.Min(int.MaxValue, (long)current + Math.Max(0, delta));
    }
}
