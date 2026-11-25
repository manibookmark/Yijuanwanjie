using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// One card's deterministic stat projection. Owns independent player, Run,
    /// deck-stat and crawler snapshots; configurations are read-only shared.
    /// The lifecycle owners perform their normal cap/entry/expiry rules on these
    /// copies. Resolution order is pre-play seals, host effects, deferred stats,
    /// post-play seals, wuxing, then the pre-existing residents' color reactions.
    /// Damage-dependent costs/rewards, random draws and external event callbacks
    /// are outside this projection, as they are outside a deterministic buff gain.
    /// </summary>
    internal sealed class CardBuffProjection
    {
        private readonly PlayerStats _player;
        private readonly RunProgressionState _run = new RunProgressionState();
        private readonly DeckManager _deck;
        private readonly ColorTriggerSystem _crawlers;
        private readonly Func<string, CrawlerConfig> _crawlerResolver;
        private readonly int _greedCap;
        private readonly Dictionary<StatType, int> _before = new Dictionary<StatType, int>();
        private readonly List<StatType> _observed = new List<StatType>();
        private readonly List<int> _initialCrawlerRuntimeIds = new List<int>();

        internal CardBuffProjection(CombatState state, RunProgressionState run,
            int greedCap, Func<string, CrawlerConfig> crawlerResolver)
        {
            PlayerStats source = state.Player;
            _player = new PlayerStats
            {
                fengRui = source.fengRui, amount = source.amount,
                bengLie = source.bengLie, armor = source.armor,
                tianYun = source.tianYun, lianYu = source.lianYu,
                mingWu = source.mingWu, hunHun = source.hunHun,
                faLi = source.faLi, zhaoSha = source.zhaoSha,
                recovery = source.recovery
            };
            _run.Restore(run.Capture());
            _run.AdditionalStats.Restore(run.AdditionalStats.Capture());
            _run.RevivalReserve.Restore(run.RevivalReserve.Capture());
            _run.RevivalReserve.InitializeOnce(Math.Max(0, source.hunHun));
            _player.hunHun = _run.RevivalReserve.RemainingPercent;
            _deck = new DeckManager(Array.Empty<CardConfig>(), state.Deck.HandLimit,
                instanceIds: new SeededInstanceIdGenerator(1),
                explicitDrawBonus: state.Deck.ExplicitDrawBonus);
            _crawlers = state.ColorTrigger.CreateProjection();
            foreach (ColorTriggerSystem.ActiveCrawler crawler in state.ColorTrigger.ActiveCrawlers)
                if (!crawler.IsExpired) _initialCrawlerRuntimeIds.Add(crawler.RuntimeId);
            _crawlerResolver = crawlerResolver;
            _greedCap = greedCap;
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                _before[stat] = Read(stat);
        }

        internal List<BuffPreview> Project(CardConfig card, int comboMultiplier,
            KeyValuePair<StatType, int>? wuxingGain)
        {
            var boundary = _crawlers.CaptureTriggerBoundary();
            ApplyPrePlaySeals(card);
            ApplyHost(card, Math.Max(1, comboMultiplier));
            foreach (CardEffectSpec effect in CardSealRules.GetPostPlayEffects(card.seals))
                ApplyImmediate(effect, effect.amount);
            if (wuxingGain.HasValue)
                Modify(wuxingGain.Value.Key, wuxingGain.Value.Value);

            // Use the real consuming path only on the independent snapshot. In
            // particular, the last reaction expires before duration follow-ups.
            var triggers = _crawlers.OnCardPlayed(card, comboMultiplier, boundary);
            if (triggers.Count > 0) Observe(StatType.NingShen);
            foreach (ColorTriggerSystem.TriggerResult trigger in triggers)
                foreach (KeyValuePair<StatType, int> delta in trigger.StatDeltas)
                    Modify(delta.Key, delta.Value);

            var results = new List<BuffPreview>();
            // ArmorStrike may be cancelled if the host kills the last target.
            // Do not label a damage-dependent final armor value as exact.
            bool damageDependentArmor = card.seals != null && card.seals.Exists(
                seal => seal != null && seal.effect == CardSealEffect.ArmorStrike && seal.value > 0);
            foreach (StatType stat in _observed)
            {
                if (stat == StatType.HuJia && damageDependentArmor) continue;
                int current = _before[stat];
                int projected = Read(stat);
                if (projected == current) continue;
                results.Add(new BuffPreview
                {
                    statType = stat, currentValue = current,
                    projectedValue = projected, delta = projected - current
                });
            }
            return results;
        }

        internal IReadOnlyList<KeyValuePair<int, int>> ProjectCrawlerDurations(
            CardConfig card, int comboMultiplier, KeyValuePair<StatType, int>? wuxingGain)
        {
            // Run the same independent settlement as the global buff preview.
            // An expired instance must still appear here with zero remaining.
            Project(card, comboMultiplier, wuxingGain);
            var remaining = new Dictionary<int, int>();
            foreach (ColorTriggerSystem.ActiveCrawler crawler in _crawlers.ActiveCrawlers)
                remaining[crawler.RuntimeId] = crawler.remainingTriggers;
            var previews = new List<KeyValuePair<int, int>>();
            foreach (int runtimeId in _initialCrawlerRuntimeIds)
                previews.Add(new KeyValuePair<int, int>(runtimeId,
                    remaining.TryGetValue(runtimeId, out int value) ? value : 0));
            return previews;
        }

        private void ApplyPrePlaySeals(CardConfig card)
        {
            if (card.seals == null) return;
            foreach (CardSealSpec seal in card.seals)
            {
                if (seal == null || seal.value <= 0) continue;
                if (seal.effect == CardSealEffect.Luck)
                    Modify(StatType.TianYun, seal.value);
                else if (seal.effect == CardSealEffect.Greed)
                    Modify(StatType.LianYu, seal.value);
            }
        }

        private void ApplyHost(CardConfig card, int multiplier)
        {
            if (!card.HasSpecEffects)
            {
                if (card.armor > 0) Modify(StatType.HuJia, card.armor * multiplier);
                return;
            }
            var deferred = new List<KeyValuePair<StatType, int>>();
            foreach (CardEffectSpec effect in card.effects)
            {
                if (effect == null) continue;
                int amount = effect.comboScalable ? effect.amount * multiplier : effect.amount;
                if (effect.effectType == EffectType.ModifyStat && effect.statType != StatType.NingShen)
                    deferred.Add(new KeyValuePair<StatType, int>(effect.statType, amount));
                else
                    ApplyImmediate(effect, amount);
            }
            foreach (KeyValuePair<StatType, int> delta in deferred)
                Modify(delta.Key, delta.Value);
        }

        private void ApplyImmediate(CardEffectSpec effect, int amount)
        {
            switch (effect.effectType)
            {
                case EffectType.GainArmor:
                    Modify(StatType.HuJia, amount);
                    break;
                case EffectType.ModifyStat:
                    Modify(effect.statType, amount);
                    break;
                case EffectType.ExtendCrawlerDuration:
                    Modify(StatType.NingShen, amount);
                    break;
                case EffectType.IncreaseCrawlerDuration:
                    _crawlers.IncreaseRemainingTriggers(effect.crawlerContentId, amount);
                    break;
                case EffectType.SummonCrawler:
                    CrawlerConfig crawler = _crawlerResolver?.Invoke(effect.crawlerContentId);
                    if (crawler == null) break;
                    Observe(StatType.NingShen);
                    _crawlers.SummonCrawler(crawler);
                    break;
            }
        }

        private void Observe(StatType stat)
        {
            if (!_observed.Contains(stat)) _observed.Add(stat);
        }

        private void Modify(StatType stat, int amount)
        {
            Observe(stat);
            switch (stat)
            {
                case StatType.FengRui: _player.fengRui += amount; break;
                case StatType.Amount: _player.amount += amount; break;
                case StatType.BengLie: _player.bengLie += amount; break;
                case StatType.NingShen: _crawlers.ResolveDurationGain(amount); break;
                case StatType.ShouPai: _deck.IncreaseHandLimit(amount); break;
                case StatType.ChouPai: _deck.IncreaseExplicitDrawBonus(amount); break;
                case StatType.TianYun: _run.AddLuck(amount); break;
                case StatType.LianYu: _run.AddGreed(amount, _greedCap); break;
                case StatType.MingWu:
                    _run.AddGrowth(amount, Math.Max(0, RunProgressionState.DefaultGrowthCap - Math.Max(0, _player.mingWu)));
                    break;
                case StatType.HunHun:
                    _run.RevivalReserve.AddCharge(amount);
                    _player.hunHun = _run.RevivalReserve.RemainingPercent;
                    break;
                case StatType.FaLi: _player.faLi += amount; break;
                case StatType.HuJia: _player.armor += amount; break;
                case StatType.ZhaoSha: _player.zhaoSha += amount; break;
                case StatType.Recovery: _player.recovery += amount; break;
            }
        }

        private int Read(StatType stat)
        {
            switch (stat)
            {
                case StatType.FengRui: return _player.fengRui;
                case StatType.Amount: return _player.amount;
                case StatType.BengLie: return _player.bengLie;
                case StatType.NingShen: return _crawlers.PendingDurationBonus;
                case StatType.ShouPai: return _deck.HandLimit;
                case StatType.ChouPai: return _deck.ExplicitDrawBonus;
                case StatType.TianYun: return _run.ProjectLuck(_player.tianYun);
                case StatType.LianYu: return _run.ProjectGreed(_player.lianYu);
                case StatType.MingWu: return _run.ProjectGrowth(_player.mingWu);
                case StatType.HunHun: return _player.hunHun;
                case StatType.FaLi: return _player.faLi;
                case StatType.HuJia: return _player.armor;
                case StatType.ZhaoSha: return _player.zhaoSha;
                case StatType.Recovery: return _player.recovery;
                default: return 0;
            }
        }
    }
}
