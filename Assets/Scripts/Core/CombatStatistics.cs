using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WanjieScroll.Core
{
    /// <summary>单张稳定卡牌内容在一场战斗中的权威表现统计。</summary>
    public readonly struct CombatCardStatistics
    {
        public CombatCardStatistics(
            string contentId,
            int timesPlayed,
            int damageDealt)
        {
            ContentId = contentId ?? string.Empty;
            TimesPlayed = Math.Max(0, timesPlayed);
            DamageDealt = Math.Max(0, damageDealt);
        }

        public string ContentId { get; }
        public int TimesPlayed { get; }
        public int DamageDealt { get; }
        public float AverageDamage => TimesPlayed > 0
            ? (float)DamageDealt / TimesPlayed
            : 0f;
    }

    /// <summary>
    /// 单场战斗统计的不可变快照。只记录已经由 Core 提交的事件，不从 UI、日志或
    /// 最终生命反推过程数据。
    /// </summary>
    public sealed class CombatStatisticsSnapshot
    {
        internal CombatStatisticsSnapshot(
            int enemiesDefeated,
            int maxCombo,
            int totalHealing,
            int freeCardsPlayed,
            int crawlerActionsTriggered,
            IReadOnlyList<CombatCardStatistics> cards, long playerXPGathered = 0)
        {
            EnemiesDefeated = Math.Max(0, enemiesDefeated);
            MaxCombo = Math.Max(0, maxCombo);
            TotalHealing = Math.Max(0, totalHealing);
            FreeCardsPlayed = Math.Max(0, freeCardsPlayed);
            CrawlerActionsTriggered = Math.Max(0, crawlerActionsTriggered);
            if (playerXPGathered < 0) throw new ArgumentOutOfRangeException(nameof(playerXPGathered));
            PlayerXPGathered = playerXPGathered;
            Cards = CopyCards(cards);
        }

        public long PlayerXPGathered { get; }
        public int EnemiesDefeated { get; }
        public int MaxCombo { get; }
        public int TotalHealing { get; }
        public int FreeCardsPlayed { get; }
        public int CrawlerActionsTriggered { get; }
        public IReadOnlyList<CombatCardStatistics> Cards { get; }

        private static IReadOnlyList<CombatCardStatistics> CopyCards(
            IReadOnlyList<CombatCardStatistics> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<CombatCardStatistics>();
            var copy = new CombatCardStatistics[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i];
            return new ReadOnlyCollection<CombatCardStatistics>(copy);
        }
    }

    /// <summary>
    /// 单场战斗统计账本的唯一写入者。它隐藏卡牌实例到稳定内容 ID 的映射，
    /// 并把延迟状态/地形伤害继续归入最初的卡牌内容。
    /// </summary>
    internal sealed class CombatStatisticsTracker
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

        private readonly Dictionary<int, string> _contentIdByInstanceId =
            new Dictionary<int, string>();
        private readonly Dictionary<string, MutableCardStatistics> _cards =
            new Dictionary<string, MutableCardStatistics>(
                StringComparer.OrdinalIgnoreCase);
        private int _enemiesDefeated;
        private int _maxCombo;
        private int _totalHealing;
        private int _freeCardsPlayed;
        private int _crawlerActionsTriggered;
        private long _playerXPGathered;

        internal void RecordCardStarted(
            CardConfig card,
            int comboCount,
            bool wasFreeToPlay)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.contentId))
                return;

            string contentId = card.contentId.Trim();
            if (card.runtimeInstanceId > 0)
                _contentIdByInstanceId[card.runtimeInstanceId] = contentId;
            MutableCardStatistics cardStatistics = GetOrCreate(contentId);
            cardStatistics.TimesPlayed++;
            _maxCombo = Math.Max(_maxCombo, Math.Max(0, comboCount));
            if (wasFreeToPlay)
                _freeCardsPlayed++;
        }

        internal void RecordDamage(DamageEvent damageEvent)
        {
            if (damageEvent.AppliedDamage <= 0 ||
                damageEvent.SourceCardInstanceId <= 0 ||
                !_contentIdByInstanceId.TryGetValue(
                    damageEvent.SourceCardInstanceId,
                    out string contentId))
            {
                return;
            }

            GetOrCreate(contentId).DamageDealt += damageEvent.AppliedDamage;
        }

        internal void RecordCardHealing(int actualHealing)
        {
            if (actualHealing > 0)
                _totalHealing += actualHealing;
        }

        internal void RecordEnemyDefeated()
            => _enemiesDefeated++;

        // Count only the real XP commit, never queued enemy awards or level-up consumption.
        internal void RecordExperienceGained(int actualExperience)
        {
            if (actualExperience < 0) throw new ArgumentOutOfRangeException(nameof(actualExperience));
            _playerXPGathered = checked(_playerXPGathered + actualExperience);
        }

        internal void RecordCrawlerTriggered()
            => _crawlerActionsTriggered++;

        internal CombatStatisticsSnapshot Capture()
        {
            var ids = new List<string>(_cards.Keys);
            ids.Sort(StringComparer.Ordinal);
            var cards = new List<CombatCardStatistics>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                MutableCardStatistics card = _cards[ids[i]];
                cards.Add(new CombatCardStatistics(
                    card.ContentId,
                    card.TimesPlayed,
                    card.DamageDealt));
            }
            return new CombatStatisticsSnapshot(
                _enemiesDefeated,
                _maxCombo,
                _totalHealing,
                _freeCardsPlayed,
                _crawlerActionsTriggered,
                cards, _playerXPGathered);
        }

        private MutableCardStatistics GetOrCreate(string contentId)
        {
            if (!_cards.TryGetValue(
                    contentId,
                    out MutableCardStatistics card))
            {
                card = new MutableCardStatistics(contentId);
                _cards.Add(contentId, card);
            }
            return card;
        }
    }
}
