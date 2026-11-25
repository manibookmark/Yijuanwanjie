using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>Detached result of one Muddle draw; an unchanged color is still one accepted draw.</summary>
    public readonly struct CrawlerTriggerColorChange
    {
        public int CrawlerRuntimeId { get; }
        public string CrawlerContentId { get; }
        public CardColor BeforeColor { get; }
        public CardColor AfterColor { get; }
        public bool Changed => BeforeColor != AfterColor;

        internal CrawlerTriggerColorChange(int runtimeId, string contentId,
            CardColor beforeColor, CardColor afterColor)
        {
            CrawlerRuntimeId = runtimeId;
            CrawlerContentId = contentId;
            BeforeColor = beforeColor;
            AfterColor = afterColor;
        }
    }

    /// <summary>
    /// 英灵跨遭遇驻留的纯数据快照。只保存稳定 contentId 和剩余凝神，
    /// 不把 ScriptableObject 引用泄漏到 Run 存档。
    /// </summary>
    public readonly struct CrawlerResidenceSnapshot
    {
        public CrawlerResidenceSnapshot(
            string contentId,
            int remainingTriggers)
        {
            ContentId = contentId;
            RemainingTriggers = remainingTriggers;
        }

        public string ContentId { get; }
        public int RemainingTriggers { get; }
    }

    /// <summary>
    /// 颜色触发系统
    /// 出牌颜色触发在场英灵的被动。
    /// 规则见 docs/rules/五行与颜色触发规范.md §4：
    /// 英灵通常只响应与其触发色一致的出牌；太极牌（Wild）
    /// 作为明确例外响应所有在场英灵，保证 Hover 预览与实际结算一致。
    /// 本类拥有驻场凝神与颜色触发；无人驻场的单次储备由 Run 提供的
    /// CrawlerDurationReserve 独占，经诀入口与五行都只获得一次凝神，不作为
    /// 每次召唤重复追加的 PlayerStats 底值。
    /// 文件规模复核：本类只在既有召唤/续时边界调用储备 owner，持久化与
    /// 跨遭遇状态不在本类解释，仍属于一个驻场生命周期边界。
    /// </summary>
    public class ColorTriggerSystem
    {
        // This module remains the single residence/reaction authority beyond 500 lines.
        // Muddle changes the condition of that same resident state, never a second
        // lifecycle or durable ledger. Re-review before adding independent event state.
        public const int MaxRemainingTriggers = CrawlerDurationReserve.MaximumBonus;

        /// <summary>
        /// 一张牌开始结算时的英灵在场边界。控制器先捕获边界、再执行卡牌效果，
        /// 结算颜色被动时便不会让本牌刚召出的英灵追溯触发同一张牌。
        /// </summary>
        public readonly struct TriggerBoundary
        {
            internal TriggerBoundary(int maximumRuntimeId)
            {
                MaximumRuntimeId = maximumRuntimeId;
            }

            internal int MaximumRuntimeId { get; }
        }

        /// <summary>
        /// 在场英灵列表（运行时实例）
        /// </summary>
        public class ActiveCrawler
        {
            public int RuntimeId { get; }
            public CrawlerConfig config { get; }
            public int remainingTriggers { get; private set; }
            public CardColor? TriggerColorOverride { get; private set; }
            public CardColor EffectiveTriggerColor => TriggerColorOverride ?? config.triggerColor;

            internal void SetTriggerColorOverride(CardColor? color) => TriggerColorOverride = color;

            public ActiveCrawler(
                CrawlerConfig config,
                int bonusTriggers = 0,
                int runtimeId = 0)
            {
                RuntimeId = runtimeId;
                this.config = config;
                remainingTriggers = Math.Min(
                    MaxRemainingTriggers,
                    Math.Max(1, config.duration + Math.Max(0, bonusTriggers)));
            }

            internal static ActiveCrawler Restore(
                CrawlerConfig config,
                int remainingTriggers,
                int runtimeId)
            {
                var crawler = new ActiveCrawler(
                    config,
                    bonusTriggers: 0,
                    runtimeId);
                crawler.remainingTriggers = Math.Min(
                    MaxRemainingTriggers,
                    Math.Max(1, remainingTriggers));
                return crawler;
            }

            public bool IsExpired => remainingTriggers <= 0;

            internal void ConsumeTrigger()
            {
                remainingTriggers = Math.Max(0, remainingTriggers - 1);
                if (IsExpired) TriggerColorOverride = null;
            }

            internal int IncreaseTriggers(int amount)
            {
                if (amount <= 0 || IsExpired)
                    return 0;

                int before = remainingTriggers;
                remainingTriggers = (int)Math.Min(
                    MaxRemainingTriggers,
                    (long)remainingTriggers + amount);
                return remainingTriggers - before;
            }

        }

        /// <summary>
        /// 一次已按触发牌倍率编译的显式抽牌请求。这里只保存基础张数，
        /// 引卷与牌区实际可抽数量由 DeckManager.DrawExplicit 统一处理。
        /// </summary>
        public readonly struct ExplicitDrawRequest
        {
            internal ExplicitDrawRequest(int baseCount)
            {
                BaseCount = baseCount;
            }

            public int BaseCount { get; }
        }

        /// <summary>
        /// 一次颜色触发的结算结果。效果只在此处按触发牌倍率快照缩放；
        /// 属性与牌区仍由各自所有者写入，不把抽牌伪装成引卷属性增量。
        /// </summary>
        public sealed class TriggerResult
        {
            public ActiveCrawler Crawler;
            public int ComboMultiplier;
            public List<KeyValuePair<StatType, int>> StatDeltas = new List<KeyValuePair<StatType, int>>();
            public IReadOnlyList<ExplicitDrawRequest> DrawRequests { get; internal set; }
                = Array.Empty<ExplicitDrawRequest>();
            public IReadOnlyList<CrawlerDurationChange> CrawlerDurationChanges { get; internal set; }
                = Array.Empty<CrawlerDurationChange>();
            public int DurationReserveGained { get; internal set; }
            public int ResultingDurationReserve { get; internal set; }
        }

        private readonly List<ActiveCrawler> _activeCrawlers = new List<ActiveCrawler>();
        private bool _randomizingTriggerColors;
        private readonly CrawlerDurationReserve _durationReserve;
        private int _nextRuntimeId;

        /// <summary>本 Run 内仅下一次召唤消费的无人驻场凝神储备。</summary>
        public int PendingDurationBonus => _durationReserve.PendingBonus;

        /// <summary>
        /// 独立只读投影副本，复用真实召唤、续时、扣次与退出规则。
        /// 配置只读共享；储备、实例身份边界和剩余次数全部独立，不复制订阅者。
        /// </summary>
        internal ColorTriggerSystem CreateProjection()
        {
            var projection = new ColorTriggerSystem(null);
            projection._durationReserve.Restore(PendingDurationBonus);
            projection._nextRuntimeId = _nextRuntimeId;
            foreach (ActiveCrawler crawler in _activeCrawlers)
                if (!crawler.IsExpired)
                {
                    ActiveCrawler restored = ActiveCrawler.Restore(
                        crawler.config, crawler.remainingTriggers, crawler.RuntimeId);
                    restored.SetTriggerColorOverride(crawler.TriggerColorOverride);
                    projection._activeCrawlers.Add(restored);
                }
            return projection;
        }

        public event Action<string> OnTrigger;

        public ColorTriggerSystem(PlayerStats player, CrawlerDurationReserve durationReserve = null)
        {
            _durationReserve = durationReserve ?? new CrawlerDurationReserve();
            // Direct/F7 model entries may still carry authored account Duration.
            // Transfer it once to the Run owner; a successful summon consumes it.
            // Formal Run entry already transfers its frozen baseline in Prepare.
            if (player != null)
            {
                _durationReserve.Bank(Math.Max(0, player.ningShen));
                player.ningShen = 0;
            }
        }

        public IReadOnlyList<ActiveCrawler> ActiveCrawlers => _activeCrawlers.AsReadOnly();

        /// <summary>
        /// Draw once per current resident in source Armor/Attack/Mana/Special order
        /// (Tu/Huo/Shui/Mu), with replacement. Prepare every draw before changing any
        /// condition. RNG failure or an out-of-range result changes no override;
        /// random-source consumption itself is caller-owned and is not rewound.
        /// </summary>
        public IReadOnlyList<CrawlerTriggerColorChange> RandomizeActiveTriggerColors(IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            RequireTriggerMutationAllowed();
            var residents = new List<ActiveCrawler>();
            var beforeColors = new List<CardColor>();
            foreach (ActiveCrawler resident in _activeCrawlers)
            {
                if (resident.IsExpired) continue;
                if (resident.config == null || !Enum.IsDefined(typeof(CardColor), resident.EffectiveTriggerColor))
                    throw new InvalidOperationException("A resident has no valid trigger condition.");
                residents.Add(resident);
                beforeColors.Add(resident.EffectiveTriggerColor);
            }
            if (residents.Count == 0) return Array.Empty<CrawlerTriggerColorChange>();

            var results = new List<CrawlerTriggerColorChange>(residents.Count);
            _randomizingTriggerColors = true;
            try
            {
                for (int i = 0; i < residents.Count; i++)
                {
                    int index = random.Range(0, 4);
                    CardColor color;
                    switch (index)
                    {
                        case 0: color = CardColor.Tu; break;
                        case 1: color = CardColor.Huo; break;
                        case 2: color = CardColor.Shui; break;
                        case 3: color = CardColor.Mu; break;
                        default: throw new InvalidOperationException("Muddle RNG returned an index outside [0,4).");
                    }
                    results.Add(new CrawlerTriggerColorChange(residents[i].RuntimeId,
                        residents[i].config.contentId, beforeColors[i], color));
                }
                // No user callbacks below this boundary. A callback cannot publish
                // a partially randomized set through any mutation entry point.
                for (int i = 0; i < residents.Count; i++)
                    if (residents[i].config == null || residents[i].IsExpired ||
                        residents[i].EffectiveTriggerColor != beforeColors[i])
                        throw new InvalidOperationException("The resident trigger condition changed during Muddle preparation.");
                for (int i = 0; i < residents.Count; i++)
                    residents[i].SetTriggerColorOverride(results[i].AfterColor);
                return results.AsReadOnly();
            }
            finally { _randomizingTriggerColors = false; }
        }

        /// <summary>Terminal/aborted encounter cleanup; residence and remaining charges are retained.</summary>
        public void ResetActiveTriggerColors()
        {
            RequireTriggerMutationAllowed();
            foreach (ActiveCrawler resident in _activeCrawlers)
                resident.SetTriggerColorOverride(null);
        }

        private void RequireTriggerMutationAllowed()
        {
            if (_randomizingTriggerColors)
                throw new InvalidOperationException("A Muddle randomization is already preparing resident conditions.");
        }

        /// <summary>
        /// 捕获当前已经在场的英灵集合边界。返回值是不透明令牌，调用方无需了解
        /// 英灵运行时身份或列表的增删细节。
        /// </summary>
        public TriggerBoundary CaptureTriggerBoundary()
            => new TriggerBoundary(_nextRuntimeId);

        /// <summary>
        /// 查询指定稳定 contentId 的英灵是否仍在场。调用方不需要了解
        /// 在场集合结构、过期判断或实例数量。
        /// </summary>
        public bool HasActiveCrawler(string crawlerContentId)
        {
            if (string.IsNullOrWhiteSpace(crawlerContentId))
                return false;

            for (int i = 0; i < _activeCrawlers.Count; i++)
            {
                ActiveCrawler crawler = _activeCrawlers[i];
                if (!crawler.IsExpired &&
                    string.Equals(
                        crawler.config?.contentId,
                        crawlerContentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 召唤英灵入场，只有下一次召唤消费无人驻场时获得的凝神储备。
        /// 经诀、五行与卡牌共用这一 owner，之后的召唤不再重复继承。
        /// </summary>
        public void SummonCrawler(CrawlerConfig crawler)
        {
            RequireTriggerMutationAllowed();
            if (crawler == null)
                throw new ArgumentNullException(nameof(crawler));
            _activeCrawlers.Add(new ActiveCrawler(
                crawler,
                PendingDurationBonus,
                ++_nextRuntimeId));
            _durationReserve.Consume();
        }

        /// <summary>
        /// 从 Run 快照恢复尚未离场的英灵。恢复时不再叠加基础凝神或
        /// 玩家凝神，剩余次数必须与上一场战斗结束时完全一致。
        /// </summary>
        internal void RestoreActiveCrawlers(
            IReadOnlyList<CrawlerResidenceSnapshot> snapshots,
            Func<string, CrawlerConfig> crawlerResolver)
        {
            RequireTriggerMutationAllowed();
            if (snapshots == null || snapshots.Count == 0)
                return;
            if (crawlerResolver == null)
                throw new InvalidOperationException(
                    "恢复英灵驻留状态时缺少内容解析器。");

            for (int i = 0; i < snapshots.Count; i++)
            {
                CrawlerResidenceSnapshot snapshot = snapshots[i];
                if (string.IsNullOrWhiteSpace(snapshot.ContentId) ||
                    snapshot.RemainingTriggers <= 0)
                {
                    continue;
                }

                CrawlerConfig crawler = crawlerResolver(snapshot.ContentId);
                if (crawler == null)
                {
                    throw new InvalidOperationException(
                        $"无法恢复英灵驻留，内容不存在：" +
                        snapshot.ContentId);
                }

                _activeCrawlers.Add(ActiveCrawler.Restore(
                    crawler,
                    snapshot.RemainingTriggers,
                    ++_nextRuntimeId));
            }
        }

        /// <summary>
        /// 给指定稳定 contentId 的所有在场英灵增加剩余触发次数。
        /// 不复活已离场英灵，并将单个实例限制在三位数内。
        /// 返回所有匹配实例实际增加的总次数。
        /// </summary>
        public int IncreaseRemainingTriggers(
            string crawlerContentId,
            int amount)
            => ResolveDurationIncrease(crawlerContentId, amount).TotalApplied;

        /// <summary>指定英灵的续时回执；不匹配时不改为无人驻场储备。</summary>
        public CrawlerDurationGainResult ResolveDurationIncrease(
            string crawlerContentId,
            int amount)
        {
            RequireTriggerMutationAllowed();
            var changes = new List<CrawlerDurationChange>();
            if (string.IsNullOrWhiteSpace(crawlerContentId) || amount <= 0)
                return new CrawlerDurationGainResult(changes, 0, PendingDurationBonus);

            for (int i = 0; i < _activeCrawlers.Count; i++)
            {
                ActiveCrawler crawler = _activeCrawlers[i];
                if (crawler.IsExpired ||
                    !string.Equals(
                        crawler.config?.contentId,
                        crawlerContentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int applied = crawler.IncreaseTriggers(amount);
                if (applied > 0)
                    changes.Add(new CrawlerDurationChange(crawler.RuntimeId,
                        crawler.config.contentId, applied, crawler.remainingTriggers));
            }
            return new CrawlerDurationGainResult(changes, 0, PendingDurationBonus);
        }

        /// <summary>
        /// VC 的通用 Duration 获得规则：有人驻场则给每个活跃英灵续时，
        /// 不增加玩家永久底值；无人驻场则储存给下一次召唤。已到期实例
        /// 不复活，鸟类驻场不属于英灵集合，不受此规则影响。
        /// </summary>
        public int ExtendRemainingTriggersOrBank(int amount)
            => ResolveDurationGain(amount).TotalApplied;

        /// <summary>
        /// Produces the gain receipt while this owner commits each capped gain.
        /// Later color triggers may consume Duration without changing this snapshot.
        /// </summary>
        public CrawlerDurationGainResult ResolveDurationGain(int amount)
        {
            RequireTriggerMutationAllowed();
            var changes = new List<CrawlerDurationChange>();
            if (amount <= 0)
                return new CrawlerDurationGainResult(changes, 0, PendingDurationBonus);
            bool hasActive = false;
            foreach (ActiveCrawler crawler in _activeCrawlers)
            {
                if (crawler.IsExpired) continue;
                hasActive = true;
                int applied = crawler.IncreaseTriggers(amount);
                if (applied > 0)
                    changes.Add(new CrawlerDurationChange(crawler.RuntimeId,
                        crawler.config.contentId, applied, crawler.remainingTriggers));
            }
            int reserveGained = hasActive ? 0 : _durationReserve.Bank(amount);
            return new CrawlerDurationGainResult(changes, reserveGained, PendingDurationBonus);
        }

        /// <summary>
        /// 出牌时触发颜色被动。
        /// comboMultiplier 为触发牌本次结算时的叠势倍率快照（见 G1F1 英灵倍率合同）。
        /// </summary>
        public List<TriggerResult> OnCardPlayed(CardConfig card, int comboMultiplier = 1)
        {
            return ResolveCardPlayed(
                card,
                comboMultiplier,
                consumeTriggers: true,
                maximumRuntimeId: _nextRuntimeId);
        }

        /// <summary>
        /// 只触发指定结算边界内已在场的英灵。用于卡牌效果可能在结算途中召唤
        /// 新英灵的路径；新英灵从下一张牌起才响应颜色。
        /// </summary>
        public List<TriggerResult> OnCardPlayed(
            CardConfig card,
            int comboMultiplier,
            TriggerBoundary boundary)
        {
            return ResolveCardPlayed(
                card,
                comboMultiplier,
                consumeTriggers: true,
                maximumRuntimeId: boundary.MaximumRuntimeId);
        }

        /// <summary>
        /// 预览本次出牌会触发的在场英灵及其效果请求。
        /// 不消耗凝神、不移除英灵，也不发布触发日志。
        /// </summary>
        public List<TriggerResult> PreviewCardPlayed(
            CardConfig card,
            int comboMultiplier = 1)
        {
            return ResolveCardPlayed(
                card,
                comboMultiplier,
                consumeTriggers: false,
                maximumRuntimeId: _nextRuntimeId);
        }

        private List<TriggerResult> ResolveCardPlayed(
            CardConfig card,
            int comboMultiplier,
            bool consumeTriggers,
            int maximumRuntimeId)
        {
            if (consumeTriggers) RequireTriggerMutationAllowed();
            var results = new List<TriggerResult>();
            if (card == null) return results;
            int multiplier = Math.Max(1, comboMultiplier);

            // Freeze trigger eligibility only. Duration recipients are the live
            // collection at each reaction, including any new resident.
            var batchCrawlers = new List<ActiveCrawler>();
            for (int i = 0; i < _activeCrawlers.Count; i++)
            {
                ActiveCrawler crawler = _activeCrawlers[i];
                if (!crawler.IsExpired && crawler.RuntimeId <= maximumRuntimeId)
                    batchCrawlers.Add(crawler);
            }

            for (int i = 0; i < batchCrawlers.Count; i++)
            {
                ActiveCrawler ac = batchCrawlers[i];
                if (ac.IsExpired)
                    continue;

                // Qualification is a union: base color, Wild and extra trigger
                // seals can never dispatch the same crawler more than once.
                if (!CardSealRules.CanTriggerColor(card, ac.EffectiveTriggerColor))
                    continue;

                var result = new TriggerResult
                {
                    Crawler = ac,
                    ComboMultiplier = multiplier,
                    ResultingDurationReserve = PendingDurationBonus
                };
                // Native FCC lifecycle consumes its charge and removes a zero
                // resident before executing the final reaction. That reaction
                // still resolves, but no subsequent gain can revive the source.
                if (consumeTriggers)
                {
                    ac.ConsumeTrigger();
                    if (ac.IsExpired)
                        _activeCrawlers.Remove(ac);
                }
                ResolveTriggerEffects(
                    ac,
                    result,
                    publishLog: consumeTriggers);
                if (consumeTriggers)
                {
                    if (HasTriggerFollowUp(ac))
                    {
                        ApplyDurationGain(result, ac.config.triggerFollowUpAmount);
                    }
                }
                results.Add(result);
            }
            return results;
        }

        private static bool HasTriggerFollowUp(ActiveCrawler crawler)
        {
            return crawler?.config != null &&
                crawler.config.triggerFollowUp != CrawlerTriggerFollowUp.None &&
                crawler.config.triggerFollowUpAmount > 0;
        }

        private void ApplyDurationGain(TriggerResult result, int amount)
        {
            CrawlerDurationGainResult gain = ResolveDurationGain(amount);
            var changes = new List<CrawlerDurationChange>(result.CrawlerDurationChanges);
            foreach (CrawlerDurationChange change in gain.CrawlerChanges)
            {
                int existing = changes.FindIndex(item => item.CrawlerRuntimeId == change.CrawlerRuntimeId);
                if (existing < 0) changes.Add(change);
                else changes[existing] = new CrawlerDurationChange(change.CrawlerRuntimeId,
                    change.CrawlerContentId, changes[existing].Amount + change.Amount, change.RemainingTriggers);
            }
            result.CrawlerDurationChanges = changes.AsReadOnly();
            result.DurationReserveGained += gain.ReserveGained;
            result.ResultingDurationReserve = gain.ResultingReserve;
        }

        /// <summary>
        /// 把英灵的结构化触发效果按倍率快照解析为属性增量或显式抽牌请求。
        /// 未配置 triggerEffects 的旧英灵仅保留日志描述，不产生数值。
        /// </summary>
        private void ResolveTriggerEffects(
            ActiveCrawler ac,
            TriggerResult result,
            bool publishLog)
        {
            string effect = ac.config.triggerEffect;
            if (publishLog)
            {
                OnTrigger?.Invoke(
                    $"  ◆ [{ac.config.crawlerName}] 触发: {effect} ×{result.ComboMultiplier}");
            }

            var specs = ac.config.triggerEffects;
            if (specs == null) return;
            var drawRequests = new List<ExplicitDrawRequest>();
            for (int i = 0; i < specs.Count; i++)
            {
                CardEffectSpec spec = specs[i];
                if (spec == null) continue;
                if (spec.effectType == EffectType.ModifyStat)
                {
                    int scaled = spec.comboScalable
                        ? spec.amount * result.ComboMultiplier
                        : spec.amount;
                    if (spec.statType == StatType.NingShen)
                    {
                        // Duration is a lifecycle gain, never a persistent stat
                        // delta. Only the committing reaction writes the owner.
                        if (publishLog) ApplyDurationGain(result, scaled);
                        continue;
                    }
                    if (scaled != 0)
                        result.StatDeltas.Add(
                            new KeyValuePair<StatType, int>(spec.statType, scaled));
                }
                else if (spec.effectType == EffectType.DrawCards && spec.count > 0)
                {
                    long baseCount = spec.comboScalable
                        ? (long)spec.count * result.ComboMultiplier
                        : spec.count;
                    drawRequests.Add(new ExplicitDrawRequest(
                        (int)Math.Min(int.MaxValue, baseCount)));
                }
            }
            result.DrawRequests = drawRequests.AsReadOnly();
        }

        public void Clear()
        {
            ResetActiveTriggerColors();
            _activeCrawlers.Clear();
        }

        public string GetSummary()
        {
            if (_activeCrawlers.Count == 0) return "无英灵在场";
            var parts = new List<string>();
            foreach (var ac in _activeCrawlers)
            {
                parts.Add($"{ac.config.crawlerName}({ac.remainingTriggers})");
            }
            return $"英灵: {string.Join(", ", parts)}";
        }
    }
}
