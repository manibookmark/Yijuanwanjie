using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    public enum ContentValidationSeverity
    {
        Warning,
        Error
    }

    public sealed class ContentValidationIssue
    {
        public ContentValidationIssue(
            ContentValidationSeverity severity,
            string path,
            string message)
        {
            Severity = severity;
            Path = path;
            Message = message;
        }

        public ContentValidationSeverity Severity { get; }
        public string Path { get; }
        public string Message { get; }

        public override string ToString()
        {
            return $"[{Severity}] {Path}: {Message}";
        }
    }

    public sealed class ContentValidationReport
    {
        private readonly List<ContentValidationIssue> _issues = new List<ContentValidationIssue>();

        public IReadOnlyList<ContentValidationIssue> Issues => _issues;
        public bool HasErrors => _issues.Exists(issue => issue.Severity == ContentValidationSeverity.Error);

        internal void Error(string path, string message)
        {
            _issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, path, message));
        }

        internal void Warning(string path, string message)
        {
            _issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, path, message));
        }
    }

    /// <summary>
    /// 内容作者数据与稳定引用的校验边界；不持有运行时战斗状态。
    /// 规模审查：超过 800 行，仍只拥有内容作者结构与稳定引用合同知识。
    /// 索引和报告仅在一次校验内存活，没有运行时状态或生命周期；
    /// 新增独立知识、运行时状态或第二种可见范围策略时重新审查。
    /// </summary>
    public static class ContentValidator
    {
        /// <summary>Resolves author profiles into immutable Core definitions; reports structural errors unchanged.</summary>
        public delegate bool EnemyBehaviorResolver(
            string contentId, out EnemyBehaviorDefinition definition, out string error);

        public static ContentValidationReport Validate(IEnumerable<VolumeContent> volumes)
            => Validate(volumes, EnemyBehaviorProfileCatalog.TryResolve);

        /// <summary>Same author contract with an explicit profile source, allowing isolated in-memory validation.</summary>
        public static ContentValidationReport Validate(
            IEnumerable<VolumeContent> volumes, EnemyBehaviorResolver behaviorResolver)
        {
            if (volumes == null)
                throw new ArgumentNullException(nameof(volumes));
            if (behaviorResolver == null)
                throw new ArgumentNullException(nameof(behaviorResolver));

            // 校验需要先建立跨卷索引，再逐卷验证。先物化一次，避免调用方
            // 传入只能枚举一次的数据源时，第二阶段静默漏掉全部内容。
            var volumeList = new List<VolumeContent>(volumes);
            var report = new ContentValidationReport();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var volumeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var crawlerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var crawlersById = new Dictionary<string, CrawlerData>(StringComparer.OrdinalIgnoreCase);
            var commonCardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cardsById = new Dictionary<string, CardData>(
                StringComparer.OrdinalIgnoreCase);

            foreach (VolumeContent volume in volumeList)
            {
                if (volume == null) continue;
                foreach (CrawlerData crawler in volume.crawlers)
                    if (crawler != null && !string.IsNullOrWhiteSpace(crawler.contentId))
                    {
                        crawlerIds.Add(crawler.contentId);
                        if (!crawlersById.ContainsKey(crawler.contentId))
                            crawlersById.Add(crawler.contentId, crawler);
                    }
                foreach (CardData card in volume.cards)
                {
                    if (card == null || string.IsNullOrWhiteSpace(card.contentId))
                        continue;
                    cardIds.Add(card.contentId);
                    if (string.Equals(volume.volumeId, "common", StringComparison.OrdinalIgnoreCase))
                        commonCardIds.Add(card.contentId);
                    if (!cardsById.ContainsKey(card.contentId))
                        cardsById.Add(card.contentId, card);
                }
            }

            foreach (VolumeContent volume in volumeList)
            {
                if (volume == null)
                {
                    report.Error("volumes", "卷内容不能为 null。");
                    continue;
                }

                string volumePath = $"volume[{volume.volumeId}]";
                if (string.IsNullOrWhiteSpace(volume.volumeId))
                {
                    report.Error(volumePath, "volumeId 不能为空。");
                }
                else if (!volumeIds.Add(volume.volumeId))
                {
                    report.Error(volumePath, $"volumeId 重复：{volume.volumeId}。");
                }

                if (volume.schemaVersion != ContentCatalog.CurrentSchemaVersion)
                {
                    report.Error(
                        volumePath,
                        $"schemaVersion={volume.schemaVersion}，当前要求 {ContentCatalog.CurrentSchemaVersion}。");
                }

                ValidateCards(volume, ids, crawlerIds, cardIds, crawlersById, report);
                ValidateNamedItems(volume.crawlers, "crawler", volume.volumeId, item => item.contentId, item => item.name, ids, report);
                ValidateCrawlerStartingCards(
                    volume,
                    cardIds,
                    cardsById,
                    report);
                ValidateEnemies(volume, ids, commonCardIds, behaviorResolver, report);
                ValidateNamedItems(volume.relics, "relic", volume.volumeId, item => item.contentId, item => item.name, ids, report);
                ValidateNamedItems(volume.dungeons, "dungeon", volume.volumeId, item => item.contentId, item => item.name, ids, report);
                ValidateEvolutions(
                    volume,
                    ids,
                    cardIds,
                    cardsById,
                    report);
                CommonEncounterContentCompiler.ValidateVolume(volume, volumeList, ids, report);
                SideEventSourceContentCompiler.ValidateVolume(volume, volumeList, ids, report);
                // Registry declarations join the common ID index; referenced project seals
                // remain references and are not re-declared as card or volume content.
                GemSourceContentCompiler.ValidateVolume(volume, ids, report);
                DungeonSideEventTimelineContentCompiler.ValidateVolume(volume, volumeList, ids, report);
            }

            return report;
        }

        private static void ValidateCrawlerStartingCards(
            VolumeContent volume,
            HashSet<string> cardIds,
            IReadOnlyDictionary<string, CardData> cardsById,
            ContentValidationReport report)
        {
            for (int i = 0; i < volume.crawlers.Count; i++)
            {
                CrawlerData crawler = volume.crawlers[i];
                if (crawler == null) continue;

                string path = $"volume[{volume.volumeId}].crawlers[{i}]";
                ValidateComboScalingAuthoring(
                    crawler.triggerEffects,
                    $"{path}.triggerEffects",
                    report);
                ValidateCrawlerTriggerFollowUp(crawler, path, report);
                if (crawler.leaderStats != null && (crawler.leaderStats.durationModifier < 0 ||
                    crawler.leaderStats.durationModifier > CrawlerDurationReserve.MaximumBonus))
                    report.Error(path + ".leaderStats", "durationModifier 必须在公共凝神储备上限内且非负。");
                IReadOnlyList<string> supportIds =
                    crawler.startingCardContentIds;
                if (supportIds == null)
                    supportIds = Array.Empty<string>();
                bool hasStructuredPackage =
                    !string.IsNullOrWhiteSpace(crawler.cardContentId) ||
                    supportIds.Count > 0;
                if (!hasStructuredPackage) continue;

                if (string.IsNullOrWhiteSpace(crawler.cardContentId))
                {
                    report.Error(path,
                        "配置四张英灵支援牌时必须同时配置金卡 cardContentId。");
                }
                else if (!cardIds.Contains(crawler.cardContentId))
                {
                    report.Error(path,
                        $"英灵金卡不存在：{crawler.cardContentId}。");
                }

                if (supportIds.Count !=
                    CrawlerStartingDeckResolver.RequiredSupportCardCount)
                {
                    report.Error(path,
                        $"英灵必须配置 " +
                        $"{CrawlerStartingDeckResolver.RequiredSupportCardCount} " +
                        $"张支援牌，当前为 {supportIds.Count} 张。");
                }

                for (int supportIndex = 0;
                     supportIndex < supportIds.Count;
                     supportIndex++)
                {
                    string supportId = supportIds[supportIndex];
                    string supportPath =
                        $"{path}.startingCardContentIds[{supportIndex}]";
                    if (string.IsNullOrWhiteSpace(supportId))
                        report.Error(supportPath, "支援牌 contentId 不能为空。");
                    else if (!cardIds.Contains(supportId))
                        report.Error(supportPath, $"支援牌不存在：{supportId}。");
                    else if (string.Equals(
                                 supportId,
                                 crawler.cardContentId,
                                 StringComparison.OrdinalIgnoreCase))
                        report.Error(supportPath, "四张支援牌不得包含英灵金卡本体。");
                }

                if (string.IsNullOrWhiteSpace(crawler.cardContentId) ||
                    !cardsById.TryGetValue(
                        crawler.cardContentId,
                        out CardData characterCard))
                {
                    continue;
                }

                bool summonsConfiguredCrawler = false;
                if (characterCard.effects != null)
                {
                    for (int effectIndex = 0;
                         effectIndex < characterCard.effects.Count;
                         effectIndex++)
                    {
                        CardEffectSpec effect = characterCard.effects[effectIndex];
                        if (effect != null &&
                            effect.effectType == EffectType.SummonCrawler &&
                            string.Equals(
                                effect.crawlerContentId,
                                crawler.contentId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            summonsConfiguredCrawler = true;
                            break;
                        }
                    }
                }

                if (!summonsConfiguredCrawler)
                {
                    report.Error(path,
                        $"英灵金卡 {crawler.cardContentId} 必须通过 " +
                        $"SummonCrawler 关联本英灵 {crawler.contentId}。");
                }
            }
        }

        private static void ValidateCards(
            VolumeContent volume,
            HashSet<string> ids,
            HashSet<string> crawlerIds,
            HashSet<string> cardIds,
            IReadOnlyDictionary<string, CrawlerData> crawlersById,
            ContentValidationReport report)
        {
            for (int i = 0; i < volume.cards.Count; i++)
            {
                CardData card = volume.cards[i];
                string path = $"volume[{volume.volumeId}].cards[{i}]";
                ValidateIdentity(card.contentId, card.name, path, ids, report);
                try { CardOfferingAdmissionData.CompileOrUnknown(card.offeringAdmission); }
                catch (ArgumentException exception) { report.Error(path + ".offeringAdmission", exception.Message); }
                if (!CardSourceIdentityData.IsUnknown(card.sourceIdentity))
                {
                    try
                    {
                        CardSourceIdentity identity = CardSourceIdentity.Compile(card.sourceIdentity);
                        CardOfferingAdmission admission = CardOfferingAdmissionData.CompileOrUnknown(card.offeringAdmission);
                        if (admission.State != CardOfferingAdmissionState.Unknown &&
                            (admission.SourceConfigId != identity.SourceConfigId || admission.SourceClass != identity.SourceClass))
                            report.Error(path + ".sourceIdentity", "Native identity contradicts known Offering source identity/class.");
                    }
                    catch (ArgumentException exception) { report.Error(path + ".sourceIdentity", exception.Message); }
                }

                if (!Enum.TryParse(card.color, true, out CardColor _))
                    report.Error(path, $"未知卡牌颜色：{card.color}。");
                if (!ContentRuntimeFactory.TryParseStatusEffect(card.statusEffect, out _))
                    report.Error(path, $"未知状态效果：{card.statusEffect}。");
                if (!string.IsNullOrWhiteSpace(card.attackPattern)
                    && !Enum.TryParse(card.attackPattern, true, out AttackPattern _))
                    report.Error(path, $"未知攻击模式：{card.attackPattern}。");
                if (card.isWild && card.isMild)
                    report.Error(path, "isWild 与 isMild 不能同时为 true。");
                if (card.manaCost < 0)
                    report.Error(path, "manaCost 不能小于 0。");
                if (card.rewardPoolCost < -1 ||
                    (card.ordinaryRewardEnabled && card.rewardPoolCost < 0))
                    report.Error(path, "普通奖励对象必须显式配置非负rewardPoolCost。");
                if (card.rewardMinPlayerLevel < 1)
                    report.Error(path, "rewardMinPlayerLevel 必须为正数，独立于永久解锁等级。");
                if (card.rewardUnlockPlayerLevel < 0 ||
                    (card.defaultRewardUnlocked && card.rewardUnlockPlayerLevel > 0))
                    report.Error(path, "普通奖励默认资格与等级解锁条件不合法。");
                if (!string.IsNullOrWhiteSpace(card.rewardUnlockCrawlerContentId))
                {
                    if (!crawlerIds.Contains(card.rewardUnlockCrawlerContentId))
                        report.Error(path, "等级解锁引用不存在的领卷英灵：" + card.rewardUnlockCrawlerContentId);
                    if (card.rewardUnlockPlayerLevel <= 0 || !card.ordinaryRewardEnabled ||
                        card.defaultRewardUnlocked || card.rewardUnlockCondition?.IsConfigured == true)
                        report.Error(path, "领卷等级解锁必须绑定正等级及普通奖励准入，不能混用默认或行为解锁。");
                }
                if (!card.ordinaryRewardEnabled &&
                    (card.defaultRewardUnlocked || card.rewardUnlockPlayerLevel > 0 || card.rewardUnlockCondition?.IsConfigured == true))
                    report.Error(path, "普通奖励资格和条件必须属于明确准入的对象。");
                if (card.rewardUnlockCondition?.IsConfigured == true)
                {
                    string conditionError = card.rewardUnlockCondition.Validate();
                    if (conditionError != null) report.Error(path, conditionError);
                    if (card.defaultRewardUnlocked || card.rewardUnlockPlayerLevel > 0)
                        report.Error(path, "默认、等级、行为解锁条件必须互斥。");
                    foreach (string sourceId in card.rewardUnlockCondition.sourceCardContentIds ?? new List<string>())
                        if (!cardIds.Contains(sourceId))
                            report.Error(path, "行为条件引用不存在的卡牌：" + sourceId);
                }
                if (card.gemSlots < 0 ||
                    card.gemSlots > CardSealRules.MaximumSealSlots)
                {
                    report.Error(path,
                        $"gemSlots 必须在 0 到 " +
                        $"{CardSealRules.MaximumSealSlots} 之间。");
                }
                if (card.statusChance < 0f || card.statusChance > 1f)
                    report.Error(path, "statusChance 必须在 0 到 1 之间。");
                if (card.projectileCount < 1)
                    report.Error(path, "projectileCount 必须大于等于 1。");
                if (card.critChance < 0f || card.critChance > 1f)
                    report.Error(path, "critChance 必须在 0 到 1 之间。");
                if (card.critMultiplier < 1f)
                    report.Error(path, "critMultiplier 必须大于等于 1。");
                if (card.areaCoefficient < 0f)
                    report.Error(path, "areaCoefficient 不能小于 0。");
                if (!string.IsNullOrWhiteSpace(card.areaTriggerMode) &&
                    !Enum.TryParse(card.areaTriggerMode, true, out AreaTriggerMode _))
                    report.Error(path, $"未知 areaTriggerMode：{card.areaTriggerMode}。");
                if (string.Equals(card.targetPolicy, "PlayerSelected", StringComparison.OrdinalIgnoreCase))
                    report.Error(path, "首发未实现 PlayerSelected；卡牌必须使用自动 TargetPolicy。");
                ValidateAttack(card, path, report);
                if (card.attack?.replayColumnsAtPlayerTurnEnd == true &&
                    (!card.attack.IsConfigured || card.attack.repeatAtPlayerTurnEnd ||
                     card.attack.birdAttack?.enabled == true))
                    report.Error(path + ".attack", "一次列补打必须绑定有效基础Damage，不能与周期/鸟源同时启用。");
                int periodicSources = card.attack?.repeatAtPlayerTurnEnd == true ? 1 : 0;
                if (periodicSources > 0 && !card.attack.IsConfigured)
                    report.Error(path + ".attack", "周期攻击必须配置有效的基础攻击，不能只设置 repeatAtPlayerTurnEnd。");
                if (card.effects != null)
                {
                    for (int effectIndex = 0; effectIndex < card.effects.Count; effectIndex++)
                    {
                        CardEffectSpec effect = card.effects[effectIndex];
                        if (effect == null)
                            continue;
                        string effectPath = $"{path}.effects[{effectIndex}]";
                        if (effect.replayColumnsAtPlayerTurnEnd &&
                            (effect.effectType != EffectType.Damage || effect.repeatAtPlayerTurnEnd ||
                             effect.damageSpec?.birdAttack != null))
                            report.Error(effectPath, "一次列补打必须绑定独立Damage，不能与周期/鸟源同时启用。");
                        if (effect.repeatAtPlayerTurnEnd)
                        {
                            periodicSources++;
                            if (effect.effectType != EffectType.Damage)
                                report.Error(effectPath, "周期源必须绑定基础 Damage，不能标记其他效果。");
                        }
                        bool resolvedComboScalable =
                            ValidateComboScalingAuthoring(
                                effect,
                                effectPath,
                                report);
                        if (effect.effectType == EffectType.RemoveEnemyIntent)
                        {
                            if (effect.target != TargetRule.AllEnemies || effect.count <= 0)
                                report.Error(effectPath,
                                    "RemoveEnemyIntent 必须使用 AllEnemies 和正数 count。");
                            if (resolvedComboScalable)
                                report.Error(effectPath,
                                    "RemoveEnemyIntent 不受叠势加成，必须显式 Disabled。");
                            if (effect.attackIntentOnly && effect.removeAllIntents)
                                report.Error(effectPath,
                                    "attackIntentOnly 与 removeAllIntents 不能同时开启；VC 的 removeAllIntents 会跳过动作类型过滤。");
                        }
                        else if (effect.attackIntentOnly || effect.removeAllIntents)
                        {
                            report.Error(effectPath,
                                "attackIntentOnly/removeAllIntents 只能用于 RemoveEnemyIntent。");
                        }
                        if (effect.effectType ==
                                EffectType.GainExperience &&
                            effect.amount <= 0)
                        {
                            report.Error(effectPath,
                                "GainExperience.amount 必须大于 0。");
                        }
                        if (effect.effectType ==
                                EffectType.IncreaseMaxHealth)
                        {
                            if (effect.amount <= 0)
                                report.Error(effectPath,
                                    "IncreaseMaxHealth.amount 必须大于 0。");
                            if (effect.target != TargetRule.Self)
                                report.Error(effectPath,
                                    "IncreaseMaxHealth.target 必须为 Self。");
                        }
                        if (effect.effectType ==
                            EffectType.RandomizeHandCosts)
                        {
                            if (effect.target != TargetRule.Self)
                                report.Error(effectPath,
                                    "RandomizeHandCosts.target 必须为 Self。");
                            if ((!effect.randomCostRelative && effect.randomCostMin < 0) ||
                                effect.randomCostMax < effect.randomCostMin ||
                                effect.randomCostMax == int.MaxValue)
                                report.Error(effectPath,
                                    "RandomizeHandCosts 的随机费用范围无效。");
                        }
                        if (effect.effectType == EffectType.RandomizeCrawlerTriggerColors)
                        {
                            if (effect.target != TargetRule.Self || resolvedComboScalable ||
                                effect.amount != 0 || effect.count != 1 ||
                                !string.IsNullOrEmpty(effect.crawlerContentId))
                                report.Error(effectPath,
                                    "RandomizeCrawlerTriggerColors 必须为全队 Self、固定一次、无数量倍率或指定英灵。");
                        }
                        if (effect.effectType == EffectType.ExtendCrawlerDuration)
                        {
                            if (effect.amount <= 0)
                                report.Error(effectPath,
                                    "ExtendCrawlerDuration.amount 必须大于 0。");
                            if (effect.target != TargetRule.Self)
                                report.Error(effectPath,
                                    "ExtendCrawlerDuration.target 必须为 Self。");
                            if (!string.IsNullOrEmpty(effect.crawlerContentId))
                                report.Error(effectPath,
                                    "ExtendCrawlerDuration 是通用全队规则，不能绑定某个英灵。");
                        }
                        if (effect.effectType != EffectType.SummonCrawler &&
                            effect.effectType !=
                                EffectType.IncreaseCrawlerDuration)
                        {
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(effect.crawlerContentId))
                            report.Error(effectPath,
                                $"{effect.effectType} 效果必须配置 " +
                                "crawlerContentId。");
                        else if (!crawlerIds.Contains(effect.crawlerContentId))
                            report.Error(effectPath,
                                $"{effect.effectType} 引用的英灵不存在：" +
                                $"{effect.crawlerContentId}。");
                        if (effect.effectType ==
                                EffectType.IncreaseCrawlerDuration &&
                            effect.amount <= 0)
                        {
                            report.Error(effectPath,
                                "IncreaseCrawlerDuration.amount 必须大于 0。");
                        }
                    }
                }

                if (periodicSources > 1)
                    report.Error(path, "同一卡牌实例只允许一个周期攻击规格；多攻击物应配置在同一 Damage 计划中。");
                bool hasStructuredContract =
                    (card.effects != null && card.effects.Count > 0) ||
                    card.attack?.IsConfigured == true;
                foreach (string issue in
                         CardEffectAudit.GetDescriptionContractIssues(card,
                             id => crawlersById.TryGetValue(id, out CrawlerData crawler) ? crawler : null))
                {
                    if (hasStructuredContract)
                        report.Error(path, issue);
                    else
                        report.Warning(path, issue);
                }
                if (card.seals != null)
                {
                    for (int sealIndex = 0; sealIndex < card.seals.Count; sealIndex++)
                    {
                        CardSealSpec seal = card.seals[sealIndex];
                        string sealPath = $"{path}.seals[{sealIndex}]";
                        if (seal == null) report.Error(sealPath, "灵印配置不能为 null。");
                        else
                        {
                            if (string.IsNullOrWhiteSpace(seal.contentId))
                                report.Error(sealPath, "灵印必须使用稳定 contentId。");
                            if (seal.value <= 0) report.Error(sealPath, "灵印增量必须大于 0。");
                            if (seal.comboScalable) report.Error(sealPath, "天运印/敛玉印不得受连击倍率缩放。");
                        }
                    }
                }
            }
        }

        private static void ValidateCrawlerTriggerFollowUp(
            CrawlerData crawler,
            string path,
            ContentValidationReport report)
        {
            if (crawler == null || string.IsNullOrWhiteSpace(crawler.triggerFollowUp))
                return;

            if (!Enum.TryParse(
                    crawler.triggerFollowUp.Trim(),
                    ignoreCase: true,
                    out CrawlerTriggerFollowUp parsed) ||
                !Enum.IsDefined(typeof(CrawlerTriggerFollowUp), parsed) ||
                parsed == CrawlerTriggerFollowUp.None)
            {
                report.Error(
                    $"{path}.triggerFollowUp",
                    $"未知英灵触发后续：{crawler.triggerFollowUp}。");
                return;
            }

            if (crawler.triggerFollowUpAmount <= 0)
            {
                report.Error(
                    $"{path}.triggerFollowUpAmount",
                    "英灵触发后续的凝神增量必须为正数。");
            }
        }

        private static void ValidateComboScalingAuthoring(
            IReadOnlyList<CardEffectSpec> effects,
            string path,
            ContentValidationReport report)
        {
            if (effects == null) return;
            for (int index = 0; index < effects.Count; index++)
            {
                CardEffectSpec effect = effects[index];
                if (effect == null) continue;
                ValidateComboScalingAuthoring(
                    effect,
                    $"{path}[{index}]",
                    report);
            }
        }

        private static bool ValidateComboScalingAuthoring(
            CardEffectSpec effect,
            string path,
            ContentValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(effect.comboScaling))
            {
                report.Error(
                    path,
                    "权威内容必须显式配置 comboScaling=" +
                    "Default、Enabled 或 Disabled。");
                return effect.comboScalable;
            }
            if (ComboScalingAuthoring.TryResolve(
                    effect,
                    out bool comboScalable))
            {
                return comboScalable;
            }

            report.Error(
                path,
                $"comboScaling 值无效：{effect.comboScaling}；" +
                "只允许 Default、Enabled 或 Disabled。");
            return false;
        }

        private static void ValidateAttack(
            CardData card, string path, ContentValidationReport report)
        {
            CardAttackData attack = card.attack;
            if (attack == null) return;
            string attackPath = $"{path}.attack";
            ValidateBirdAttack(attack, attackPath, report);
            if (!attack.IsConfigured)
            {
                if (attack.limitBreakAttackInstancesPerLevel != 0)
                    report.Error(
                        attackPath,
                        "未配置有效 attack 时不得单独配置 " +
                        "limitBreakAttackInstancesPerLevel。");
                return;
            }
            if (attack.attackInstanceCount < 1)
                report.Error(attackPath,
                    "attackInstanceCount 必须大于等于 1。");
            if (attack.targetsPerAttack < 0)
                report.Error(attackPath, "targetsPerAttack 不能小于 0；0 表示全部可攻击目标。");
            if (!Enum.TryParse(attack.visualPattern, true, out AttackPattern _))
                report.Error(attackPath, $"未知 visualPattern：{attack.visualPattern}。");
            if (!Enum.TryParse(attack.targetPolicy, true, out TargetPolicy policy))
                report.Error(attackPath, $"未知 targetPolicy：{attack.targetPolicy}。");
            else if (policy == TargetPolicy.PlayerSelected)
                report.Error(attackPath, "首发未实现 PlayerSelected。");
            else if (policy == TargetPolicy.MultiTarget)
                report.Error(attackPath,
                    "新配置不得使用 MultiTarget；请用 LeftToRight + targetsPerAttack。");
            if (attack.critChance < 0f || attack.critChance > 1f)
                report.Error(attackPath, "critChance 必须在 0 到 1 之间。");
            if (attack.critMultiplier < 1f)
                report.Error(attackPath, "critMultiplier 必须大于等于 1。");
            if (!Enum.TryParse(attack.areaTriggerMode, true, out AreaTriggerMode _))
                report.Error(attackPath,
                    $"未知 areaTriggerMode：{attack.areaTriggerMode}。");
            if (attack.areaCoefficient < 0f)
                report.Error(attackPath, "areaCoefficient 不能小于 0。");
            if (attack.damageMultiplier < 0f)
                report.Error(attackPath, "damageMultiplier 不能小于 0。");
            if (float.IsNaN(attack.manaDamageCoefficient) || float.IsInfinity(attack.manaDamageCoefficient) ||
                attack.manaDamageCoefficient < 0f)
                report.Error(attackPath, "manaDamageCoefficient 必须为有限非负数。");
            if (attack.limitBreakAttackInstancesPerLevel < 0)
                report.Error(
                    attackPath,
                    "limitBreakAttackInstancesPerLevel 不能小于 0。");
            string comboModeName = string.IsNullOrWhiteSpace(attack.comboMode)
                ? nameof(ComboPatternMode.ScaleHitDamage)
                : attack.comboMode;
            if (!Enum.TryParse(
                    comboModeName,
                    true,
                    out ComboPatternMode comboMode))
                report.Error(attackPath, $"未知 comboMode：{attack.comboMode}。");
            else if (attack.limitBreakAttackInstancesPerLevel > 0 &&
                     comboMode != ComboPatternMode.RepeatBasePattern)
                report.Error(
                    attackPath,
                    "突破攻击物增量只允许用于 RepeatBasePattern 合同。");
            string amountModeName = string.IsNullOrWhiteSpace(attack.amountMode)
                ? nameof(AmountPatternMode.ExtraAttackInstance)
                : attack.amountMode;
            if (!Enum.TryParse(amountModeName, true, out AmountPatternMode amountMode))
                report.Error(attackPath, $"未知 amountMode：{attack.amountMode}。");
            else if (amountMode == AmountPatternMode.ScaleHitDamage)
                report.Error(attackPath,
                    "数量必须增加攻击物；新作者配置不得将 Amount 折合单击伤害，请使用 ExtraAttackInstance。");
            if (attack.bounce != null && attack.bounce.enabled)
            {
                string bounceModeName = string.IsNullOrWhiteSpace(
                        attack.bounce.sequenceMode)
                    ? nameof(BounceSequenceMode.PerDirectHit)
                    : attack.bounce.sequenceMode;
                if (!Enum.TryParse(bounceModeName, true,
                        out BounceSequenceMode bounceMode))
                {
                    report.Error(attackPath,
                        $"未知 bounce.sequenceMode：{attack.bounce.sequenceMode}。");
                }
                else if (bounceMode == BounceSequenceMode.PerDirectHit)
                {
                    if (attack.bounce.hitsPerDirectHit <= 0)
                        report.Error(attackPath,
                            "逐直击弹跳的 hitsPerDirectHit 必须大于 0。");
                }
                else if (bounceMode == BounceSequenceMode.NativePerAttackInstance)
                {
                    if (attack.bounce.hitsPerDirectHit != 0)
                        report.Error(attackPath,
                            "原生弹跳命中上限由基础攻击物数与数量派生，内容不得写死 hitsPerDirectHit。");
                    bool areaPerHit = string.Equals(attack.areaTriggerMode, "PerDirectTarget", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(attack.areaTriggerMode, "Disabled", StringComparison.OrdinalIgnoreCase);
                    bool orderedTargets = string.Equals(attack.targetPolicy, "SequentialRightToLeft", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(attack.targetPolicy, "LeftToRight", StringComparison.OrdinalIgnoreCase);
                    if (attack.targetsPerAttack != 1 || attack.canCrit || !areaPerHit || !orderedTargets ||
                        comboMode != ComboPatternMode.RepeatBasePattern ||
                        attack.terrainEffect?.IsConfigured == true || attack.ricochetProjectile?.enabled == true ||
                        (attack.onHitEffects != null && attack.onHitEffects.Count > 0))
                        report.Error(attackPath,
                            "原生弹跳要求逐物顺序选敌、单 Direct 目标、叠势增加攻击物与逐命中范围伤，不支持独立暴击、地形或控制效果。");
                }
                else
                {
                    if (attack.bounce.hitsPerDirectHit != 0)
                        report.Error(attackPath,
                            "同步波次的 Bounce 轮数由最终攻击物数派生，内容不得写死 hitsPerDirectHit。");
                    if (attack.targetsPerAttack != 1 || attack.canCrit ||
                        !string.Equals(attack.areaTriggerMode, "Disabled",
                            StringComparison.OrdinalIgnoreCase) ||
                        attack.terrainEffect?.IsConfigured == true ||
                        (attack.onHitEffects != null &&
                         attack.onHitEffects.Count > 0))
                    {
                        report.Error(attackPath,
                            "同步波次当前只支持单 Direct 目标且不能混用暴击、范围、地形或逐 Hit 控制效果。");
                    }
                }
            }
            if (attack.terrainEffect?.IsConfigured == true)
            {
                CardTerrainEffectData terrain = attack.terrainEffect;
                if (string.IsNullOrWhiteSpace(terrain.effectId))
                    report.Error(attackPath,
                        "terrainEffect.effectId 必须使用稳定 ID。");
                if (!Enum.TryParse(terrain.effectKind, true,
                        out TerrainEffectKind _))
                    report.Error(attackPath,
                        $"未知 terrainEffect.effectKind：{terrain.effectKind}。");
                if (!Enum.TryParse(terrain.triggerTiming, true,
                        out TerrainEffectTiming _))
                    report.Error(attackPath,
                        $"未知 terrainEffect.triggerTiming：{terrain.triggerTiming}。");
                if (!Enum.TryParse(terrain.damageKind, true,
                        out DamageKind _))
                    report.Error(attackPath,
                        $"未知 terrainEffect.damageKind：{terrain.damageKind}。");
                if (terrain.magnitudeScale <= 0f)
                    report.Error(attackPath,
                        "terrainEffect.magnitudeScale 必须大于 0。");
                if (terrain.triggerCount < 1)
                    report.Error(attackPath,
                        "terrainEffect.triggerCount 必须大于等于 1。");
                if (!Enum.TryParse(terrain.targetMode, true, out TerrainTargetMode targetMode))
                    report.Error(attackPath, $"未知 terrainEffect.targetMode：{terrain.targetMode}。");
                else if (targetMode == TerrainTargetMode.ColumnDynamicFront &&
                    (terrain.baseMagnitude <= 0 || terrain.areaCoefficient < 0f))
                    report.Error(attackPath, "按列动态地形必须有正baseMagnitude和非负areaCoefficient。");
                if (terrain.persistUntilCombatEnd && targetMode != TerrainTargetMode.ColumnDynamicFront)
                    report.Error(attackPath, "战斗内永久地形只允许ColumnDynamicFront。");
            }
            if (attack.ricochetProjectile?.enabled == true)
            {
                CardRicochetProjectileData ricochet = attack.ricochetProjectile;
                if (float.IsNaN(ricochet.amountSpeedCoefficient) || float.IsInfinity(ricochet.amountSpeedCoefficient) ||
                    ricochet.amountSpeedCoefficient < 0f)
                    report.Error(attackPath, "ricochetProjectile.amountSpeedCoefficient必须是有限非负数。");
                if (float.IsNaN(ricochet.minimumLifetime) || float.IsNaN(ricochet.maximumLifetime) ||
                    ricochet.minimumLifetime < 0.5f || ricochet.maximumLifetime > 1.5f ||
                    ricochet.maximumLifetime < ricochet.minimumLifetime)
                    report.Error(attackPath, "ricochetProjectile寿命必须在0.5–1.5秒闭区间。");
                if (attack.birdAttack?.enabled == true || attack.bounce?.enabled == true ||
                    attack.terrainEffect?.IsConfigured == true || attack.canCrit ||
                    attack.targetsPerAttack != 1 || (attack.onHitEffects != null && attack.onHitEffects.Count > 0))
                    report.Error(attackPath, "反弹轨迹不能混用鸟、普通弹跳、地形、暴击或控制效果。");
            }

            if (card.effects != null)
            {
                for (int i = 0; i < card.effects.Count; i++)
                    if (card.effects[i] != null &&
                        card.effects[i].effectType == EffectType.Damage)
                        report.Error(attackPath,
                            "attack 已定义伤害，不得在 effects 中重复配置 Damage。");
            }

            if (attack.onHitEffects == null) return;
            for (int i = 0; i < attack.onHitEffects.Count; i++)
            {
                CardOnHitEffectData effect = attack.onHitEffects[i];
                string effectPath = $"{attackPath}.onHitEffects[{i}]";
                if (effect == null)
                {
                    report.Error(effectPath, "命中效果不能为 null。");
                    continue;
                }
                if (!Enum.TryParse(effect.effectType, true, out OnHitEffectType _))
                    report.Error(effectPath, $"未知 effectType：{effect.effectType}。");
                if (effect.overrideChance &&
                    (effect.chance < 0f || effect.chance > 1f))
                    report.Error(effectPath, "chance 必须在 0 到 1 之间。");
                else if (!effect.overrideChance && effect.chance != 0f)
                    report.Error(effectPath,
                        "未启用 overrideChance 时不得配置 chance；默认概率由 CombatRuleSet 提供。");
            }
        }

        private static void ValidateBirdAttack(
            CardAttackData attack, string attackPath, ContentValidationReport report)
        {
            CardBirdAttackData bird = attack.birdAttack;
            if (bird?.enabled != true) return;
            string birdPath = $"{attackPath}.birdAttack";
            if (!attack.IsConfigured || !attack.repeatAtPlayerTurnEnd)
                report.Error(birdPath,
                    "birdAttack 必须绑定有效基础 Damage 与 repeatAtPlayerTurnEnd，不能作为普通冻结周期计划使用。");
            if (attack.targetsPerAttack != 1)
                report.Error(birdPath, "鸟导弹的 targetsPerAttack 必须为 1。");
            if (bird.missilesPerBird <= 0)
                report.Error(birdPath, "missilesPerBird 必须大于 0。");
            if (bird.endTurnMissilesPerBird <= 0)
                report.Error(birdPath, "endTurnMissilesPerBird 必须大于 0。");
            if (bird.endTurnDamagePerHit <= 0)
                report.Error(birdPath, "endTurnDamagePerHit 必须大于 0。");
            if (bird.endTurnAreaCoefficient < 0f ||
                float.IsNaN(bird.endTurnAreaCoefficient) ||
                float.IsInfinity(bird.endTurnAreaCoefficient))
                report.Error(birdPath, "endTurnAreaCoefficient 必须为有限非负数。");

            // 这里只检查无属性修正的作者基础规模；Combo/Amount 的实际预算由
            // Core 在构建计划时校验，不以截断鸟数或导弹数改变规则。
            long initialHits = (long)attack.attackInstanceCount * bird.missilesPerBird;
            long endTurnHits = (long)attack.attackInstanceCount * bird.endTurnMissilesPerBird;
            if (initialHits > AttackPlanBuilder.MaximumSubHitsPerPlan ||
                endTurnHits > AttackPlanBuilder.MaximumSubHitsPerPlan)
                report.Error(birdPath,
                    $"鸟攻击基础规模超过 {AttackPlanBuilder.MaximumSubHitsPerPlan} 个 SubHit 的运行时安全上限。");
        }

        private static void ValidateEnemies(
            VolumeContent volume,
            HashSet<string> ids,
            HashSet<string> commonCardIds,
            EnemyBehaviorResolver behaviorResolver,
            ContentValidationReport report)
        {
            // Factory binds temporary cards from common, then the requested container
            // volume. EnemyData.volumeId is metadata and cannot grant foreign visibility.
            var visibleCardIds = new HashSet<string>(commonCardIds, StringComparer.OrdinalIgnoreCase);
            foreach (CardData card in volume.cards)
                if (card != null && !string.IsNullOrWhiteSpace(card.contentId))
                    visibleCardIds.Add(card.contentId);

            for (int i = 0; i < volume.enemies.Count; i++)
            {
                EnemyData enemy = volume.enemies[i];
                string path = $"volume[{volume.volumeId}].enemies[{i}]";
                ValidateIdentity(
                    enemy.contentId,
                    enemy.name,
                    path,
                    ids,
                    report);

                if (!string.IsNullOrWhiteSpace(enemy.shortDescription))
                {
                    string description = enemy.shortDescription.Trim();
                    if (description.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    {
                        report.Error(path,
                            "shortDescription 必须保持单行。");
                    }
                    if (description.Length > 40)
                    {
                        report.Error(path,
                            "shortDescription 不得超过 40 个字符。");
                    }
                }

                if (!EnemyFormationLayout.TryValidateFootprint(
                        enemy.gridWidth,
                        enemy.gridDepth,
                        out string error))
                {
                    report.Error(path, error);
                }
                if (!Enum.TryParse(enemy.bodySize, true, out EnemyBodySize _))
                    report.Error(path, $"未知敌人体型：{enemy.bodySize}。");
                if (enemy.knockbackMultiplier < -1f)
                    report.Error(path, "knockbackMultiplier 必须为 -1（自动）或非负数。");
                if (enemy.disarmMultiplier < -1f)
                    report.Error(path, "disarmMultiplier 必须为 -1（自动）或非负数。");
                if (enemy.armor < 0)
                    report.Error(path, "armor 不能小于 0。");
                if (enemy.maxArmor < 0)
                    report.Error(path, "maxArmor 不能小于 0。");
                if (enemy.maxArmor > 0 && enemy.armor > enemy.maxArmor)
                    report.Error(path, "初始 armor 不能大于 maxArmor。");
                // legacy.* 是明确的迁移兼容边界，仍可暂时沿用旧随机 AI；
                // 所有正式 enemy.* 内容必须显式声明行为，避免空引用静默改变玩法。
                bool isLegacyCompatibilityEnemy =
                    !string.IsNullOrWhiteSpace(enemy.contentId) &&
                    enemy.contentId.StartsWith(
                        "legacy.",
                        StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(enemy.behaviorProfileId))
                {
                    if (!isLegacyCompatibilityEnemy)
                    {
                        report.Error(
                            path,
                            "正式敌人 behaviorProfileId 不能为空；" +
                            "legacy.* 仅作为迁移期兼容边界。");
                    }
                }
                else if (!behaviorResolver(
                             enemy.behaviorProfileId,
                             out EnemyBehaviorDefinition definition,
                             out string behaviorError))
                {
                    report.Error(path, behaviorError);
                }
                else if (definition == null)
                {
                    report.Error(path, "行为解析成功但未返回定义：" + enemy.behaviorProfileId);
                }
                else
                {
                    ValidateEnemyTemporaryCards(definition, visibleCardIds, path, report);
                }
            }
        }

        private static void ValidateEnemyTemporaryCards(
            EnemyBehaviorDefinition definition,
            HashSet<string> visibleCardIds,
            string enemyPath,
            ContentValidationReport report)
        {
            // Core owns the traversal of legacy phases and reversible health groups.
            // These flattened indexes are diagnostic locations, not production route rules.
            int index = 0;
            foreach (EnemyBehaviorAction action in definition.EnumerateActions())
            {
                string actionPath = $"{enemyPath}.behaviorProfileId.actions[{index++}]";
                if (action.IntentType == EnemyIntentType.AddEncounterTemporaryCard &&
                    !visibleCardIds.Contains(action.TemporaryCard.ContentId))
                    report.Error(actionPath + ".temporaryCardContentId",
                        $"临时牌 {action.TemporaryCard.ContentId} 在敌人所在容器卷及 common 中不可见。");
                if (action.IntentType == EnemyIntentType.Compound &&
                    action.Compound.SecondaryIntentType == EnemyIntentType.AddEncounterTemporaryCard &&
                    !visibleCardIds.Contains(action.Compound.SecondaryTemporaryCard.ContentId))
                    report.Error(actionPath + ".compoundTemporaryCardContentId",
                        $"临时牌 {action.Compound.SecondaryTemporaryCard.ContentId} 在敌人所在容器卷及 common 中不可见。");
            }
        }

        private static void ValidateEvolutions(
            VolumeContent volume,
            HashSet<string> ids,
            HashSet<string> cardIds,
            IReadOnlyDictionary<string, CardData> cardsById,
            ContentValidationReport report)
        {
            for (int i = 0; i < volume.evolutions.Count; i++)
            {
                EvolutionData evolution = volume.evolutions[i];
                string path = $"volume[{volume.volumeId}].evolutions[{i}]";
                ValidateIdentity(
                    evolution.contentId,
                    $"{evolution.primaryCardContentId}->" +
                    $"{evolution.resultCardContentId}",
                    path,
                    ids,
                    report);

                ValidateEvolutionCardReference(
                    evolution.primaryCardContentId,
                    $"{path}.primaryCardContentId",
                    volume.volumeId,
                    allowCommon: false,
                    cardIds,
                    cardsById,
                    report);
                // 化形印只使用主卡→结果卡路线。字段保留是为了旧 JSON 能反序列化，
                // 但新作者数据必须显式清空，避免旧 RefineCard 语义再次回流。
                if (evolution.secondaryCardContentIds != null &&
                    evolution.secondaryCardContentIds.Count > 0)
                {
                    report.Error(
                        $"{path}.secondaryCardContentIds",
                        "正式进化不使用副卡；历史兼容字段必须为空数组。");
                }
                if (evolution.secondaryCards != null &&
                    evolution.secondaryCards.Count > 0)
                {
                    report.Error(
                        $"{path}.secondaryCards",
                        "正式进化不使用副卡显示名；历史兼容字段必须为空数组。");
                }
                ValidateEvolutionCardReference(
                    evolution.resultCardContentId,
                    $"{path}.resultCardContentId",
                    volume.volumeId,
                    allowCommon: false,
                    cardIds,
                    cardsById,
                    report);
            }
        }

        private static void ValidateEvolutionCardReference(
            string contentId,
            string path,
            string owningVolumeId,
            bool allowCommon,
            HashSet<string> cardIds,
            IReadOnlyDictionary<string, CardData> cardsById,
            ContentValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(contentId))
            {
                report.Error(path, "进化卡牌 contentId 不能为空。");
                return;
            }
            if (!cardIds.Contains(contentId) ||
                !cardsById.TryGetValue(contentId, out CardData card))
            {
                report.Error(path, $"进化引用的卡牌不存在：{contentId}。");
                return;
            }

            bool ownsCard = string.Equals(
                card.volumeId,
                owningVolumeId,
                StringComparison.OrdinalIgnoreCase);
            bool isAllowedCommon = allowCommon && string.Equals(
                card.volumeId,
                "common",
                StringComparison.OrdinalIgnoreCase);
            if (!ownsCard && !isAllowedCommon)
            {
                report.Error(path,
                    $"进化引用 {contentId} 属于卷 {card.volumeId}，" +
                    $"不能用于卷 {owningVolumeId} 的该位置。");
            }
        }

        private static void ValidateNamedItems<T>(
            IReadOnlyList<T> items,
            string kind,
            string volumeId,
            Func<T, string> getId,
            Func<T, string> getName,
            HashSet<string> ids,
            ContentValidationReport report)
        {
            for (int i = 0; i < items.Count; i++)
            {
                ValidateIdentity(
                    getId(items[i]),
                    getName(items[i]),
                    $"volume[{volumeId}].{kind}s[{i}]",
                    ids,
                    report);
            }
        }

        private static void ValidateIdentity(
            string contentId,
            string displayName,
            string path,
            HashSet<string> ids,
            ContentValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                report.Error(path, "contentId 不能为空。");
            else if (!ids.Add(contentId))
                report.Error(path, $"contentId 重复：{contentId}。");

            if (string.IsNullOrWhiteSpace(displayName))
                report.Error(path, "显示名称不能为空。");
        }
    }
}
