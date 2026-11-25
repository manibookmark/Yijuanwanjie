using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// 把内容 DTO 转换为运行时对象。所有字段映射只允许在这里维护。
    /// 规模审查：文件超过 800 行，仍只拥有 DTO→运行时对象及稳定 ID 引用链接。
    /// 严格阵容入口复用同一映射，并保证缺失身份和失败绑定不产生部分内容；
    /// 不拥有 Run、随机流或物理阵型。若再加入独立内容策略应提取其真正 owner。
    /// </summary>
    public static class ContentRuntimeFactory
    {
        private static readonly Dictionary<string, StatusEffect> LegacyStatusEffects =
            new Dictionary<string, StatusEffect>(StringComparer.OrdinalIgnoreCase)
            {
                ["Knockback"] = StatusEffect.ZhenTui,
                ["Disarm"] = StatusEffect.JiaoXie,
                ["Erase"] = StatusEffect.MoSha,
                ["Burn"] = StatusEffect.ZhuoShao,
                ["Freeze"] = StatusEffect.BingFeng,
                ["Crit"] = StatusEffect.BaoJi
            };

        public static List<CardConfig> CreateCardPool(
            string volumeId,
            bool includeCommon = true)
        {
            ContentCatalog catalog = ContentCatalog.Current;
            VolumeContent volume = catalog.GetVolume(volumeId);
            var sources = new List<CardData>();
            var evolutions = new List<EvolutionData>();

            if (includeCommon && !string.Equals(volumeId, "common", StringComparison.OrdinalIgnoreCase))
            {
                VolumeContent common = catalog.GetVolume("common");
                sources.AddRange(common.cards);
                evolutions.AddRange(common.evolutions);
            }

            sources.AddRange(volume.cards);
            evolutions.AddRange(volume.evolutions);

            return CreateLinkedCardPool(sources, evolutions);
        }

        /// <summary>
        /// 创建整个权威内容目录的完整运行时卡池。供总录、审核与 F7 等跨卷
        /// 工具使用；所有进化引用与正式战斗卡池经过同一链接边界。
        /// </summary>
        public static List<CardConfig> CreateAllCardPool()
        {
            ContentCatalog catalog = ContentCatalog.Current;
            var sources = new List<CardData>();
            var evolutions = new List<EvolutionData>();
            foreach (VolumeContent volume in catalog.Volumes)
            {
                if (volume == null) continue;
                if (volume.cards != null)
                    sources.AddRange(volume.cards);
                if (volume.evolutions != null)
                    evolutions.AddRange(volume.evolutions);
            }
            return CreateLinkedCardPool(sources, evolutions);
        }

        private static List<CardConfig> CreateLinkedCardPool(
            IReadOnlyList<CardData> sources,
            IReadOnlyList<EvolutionData> evolutions)
        {
            var configs = new List<CardConfig>(sources.Count);
            var cardsById = new Dictionary<string, CardConfig>(
                StringComparer.OrdinalIgnoreCase);
            foreach (CardData source in sources)
            {
                CardConfig config = CreateCard(source);
                configs.Add(config);
                if (!cardsById.TryAdd(source.contentId, config))
                    throw new InvalidOperationException(
                        $"卡牌 contentId 重复，无法建立进化引用：{source.contentId}。");
            }

            LinkEvolutions(evolutions, cardsById);
            return configs;
        }

        /// <summary>
        /// Materializes a complete frozen roster in its original occurrence order.
        /// Empty input is an entry failure, never a request for default enemies.
        /// The shared mapper validates all identities before creating actors and
        /// releases any per-encounter temporary-card definitions if binding fails.
        /// </summary>
        public static List<EnemyStats> CreateRequiredEnemyLineup(
            string volumeId, IReadOnlyList<string> contentIds,
            int healthOverride = 0, int experienceOverride = 0,
            string levelBossContentId = null, BossRank levelBossRank = BossRank.Floor)
        {
            if (contentIds == null || contentIds.Count == 0)
                throw new ArgumentException("A frozen enemy roster must contain explicit enemies.", nameof(contentIds));
            return CreateEnemyLineup(volumeId, contentIds, healthOverride,
                experienceOverride, levelBossContentId, levelBossRank);
        }

        /// <summary>
        /// Creates the ordered runtime enemy lineup used by every battle host.
        /// Stable content IDs, fallback selection, DTO mapping and optional
        /// scenario health/experience overrides are intentionally centralized
        /// here so every battle host uses the same runtime enemy mapping.
        /// </summary>
        public static List<EnemyStats> CreateEnemyLineup(
            string volumeId,
            IReadOnlyList<string> contentIds = null,
            int healthOverride = 0,
            int experienceOverride = 0,
            string levelBossContentId = null,
            BossRank levelBossRank = BossRank.Floor)
        {
            ContentCatalog catalog = ContentCatalog.Current;
            VolumeContent volume = catalog.GetVolume(volumeId);
            var source = new List<EnemyData>();
            source.AddRange(volume.enemies);
            if (!string.Equals(
                    volumeId,
                    "common",
                    StringComparison.OrdinalIgnoreCase))
            {
                source.AddRange(catalog.GetVolume("common").enemies);
            }

            // Enemy behavior profiles are cached Core data.  An
            // AddEncounterTemporaryCard action still needs the current content
            // package to bind its stable card ID to a concrete definition for
            // this encounter; keep that resolver at the Content->Core boundary.
            var temporaryCardSources = new Dictionary<string, CardData>(
                StringComparer.OrdinalIgnoreCase);
            void AddTemporaryCardSources(VolumeContent candidate)
            {
                if (candidate?.cards == null) return;
                for (int i = 0; i < candidate.cards.Count; i++)
                {
                    CardData card = candidate.cards[i];
                    if (card != null &&
                        !string.IsNullOrWhiteSpace(card.contentId))
                    {
                        temporaryCardSources[card.contentId] = card;
                    }
                }
            }
            AddTemporaryCardSources(catalog.GetVolume("common"));
            if (!string.Equals(
                    volumeId,
                    "common",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddTemporaryCardSources(volume);
            }
            var allocatedTemporaryCards = new List<CardConfig>();
            Func<string, CardConfig> resolveTemporaryCard = contentId =>
            {
                if (string.IsNullOrWhiteSpace(contentId) ||
                    !temporaryCardSources.TryGetValue(
                        contentId,
                        out CardData card))
                {
                    return null;
                }
                CardConfig created = CreateCard(card);
                allocatedTemporaryCards.Add(created);
                return created;
            };

            var selected = new List<EnemyData>();
            if (contentIds != null && contentIds.Count > 0)
            {
                var byId = new Dictionary<string, EnemyData>(StringComparer.OrdinalIgnoreCase);
                foreach (EnemyData enemy in source) byId[enemy.contentId] = enemy;
                // Resolve the whole ordered roster before the first binding/allocation.
                // Repeated species are legitimate separate actor occurrences.
                foreach (string contentId in contentIds)
                {
                    if (string.IsNullOrWhiteSpace(contentId) || !byId.TryGetValue(contentId, out EnemyData data))
                        throw new InvalidOperationException($"战斗场景缺少敌人：{contentId}。");
                    selected.Add(data);
                }
            }
            else
            {
                foreach (EnemyData data in source)
                {
                    if (!data.isBoss && selected.Count == 0) selected.Add(data);
                    else if (data.isBoss && selected.Count == 1)
                    { selected.Add(data); break; }
                }
            }

            try
            {
                var result = new List<EnemyStats>(selected.Count);
                foreach (EnemyData data in selected)
                    result.Add(CreateEnemy(data, resolveTemporaryCard));
                if (contentIds == null || contentIds.Count == 0)
                {
                    if (result.Count == 0) result.Add(new EnemyStats("妖兵", 80, 5, 0, false));
                    if (result.Count == 1) result.Add(new EnemyStats("天劫守卫", 1500, 20, 10, true));
                }
                if (healthOverride > 0)
                    foreach (EnemyStats enemy in result)
                    {
                        enemy.baseHealth = healthOverride;
                        enemy.currentHealth = healthOverride;
                        enemy.armor = 0;
                    }
                if (experienceOverride > 0)
                    foreach (EnemyStats enemy in result) enemy.experienceReward = experienceOverride;
                MarkLevelBoss(result, levelBossContentId, levelBossRank);
                return result;
            }
            catch
            {
                foreach (CardConfig card in allocatedTemporaryCards)
                    if (card != null) UnityEngine.Object.DestroyImmediate(card, allowDestroyingAssets: false);
                throw;
            }
        }

        private static void MarkLevelBoss(
            IReadOnlyList<EnemyStats> enemies,
            string levelBossContentId,
            BossRank levelBossRank)
        {
            if (string.IsNullOrWhiteSpace(levelBossContentId)) return;
            if (levelBossRank != BossRank.Floor &&
                levelBossRank != BossRank.Stage)
                throw new InvalidOperationException(
                    $"关卡 BOSS 职责必须为 Floor 或 Stage，" +
                    $"当前为 {levelBossRank}。");

            EnemyStats levelBoss = null;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy == null || !string.Equals(
                        enemy.contentId,
                        levelBossContentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (levelBoss != null)
                {
                    throw new InvalidOperationException(
                        $"关卡 BOSS {levelBossContentId} 在本场出现了多次。");
                }
                levelBoss = enemy;
            }

            if (levelBoss == null)
                throw new InvalidOperationException(
                    $"本场敌人列表缺少关卡 BOSS：{levelBossContentId}。");
            if (!levelBoss.isBoss)
                throw new InvalidOperationException(
                    $"关卡 BOSS 必须配置 isBoss=true：{levelBossContentId}。");
            levelBoss.bossRank = levelBossRank;
        }

        public static CardConfig CreateCard(CardData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (!Enum.TryParse(data.color, true, out CardColor color))
                throw new InvalidOperationException($"卡牌 {data.name} 使用了未知颜色：{data.color}。");
            if (!TryParseStatusEffect(data.statusEffect, out StatusEffect statusEffect))
                throw new InvalidOperationException($"卡牌 {data.name} 使用了未知状态：{data.statusEffect}。");
            // JsonUtility may materialize an empty nested attack object even
            // when old non-attack content omitted the field. Only a positive
            // damage value opts a card into the structured attack pipeline.
            CardAttackData attack = data.attack?.IsConfigured == true
                    ? data.attack
                    : null;
            if (data.attack?.replayColumnsAtPlayerTurnEnd == true &&
                (attack == null || attack.repeatAtPlayerTurnEnd || attack.birdAttack?.enabled == true))
                throw new InvalidOperationException($"卡牌 {data.name} 的一次列补打必须绑定独立有效Damage。");
            if (data.attack?.birdAttack?.enabled == true &&
                (attack == null || !attack.repeatAtPlayerTurnEnd ||
                 attack.attackInstanceCount < 1 || attack.targetsPerAttack != 1 ||
                 attack.birdAttack.missilesPerBird <= 0 ||
                 attack.birdAttack.endTurnMissilesPerBird <= 0 ||
                 attack.birdAttack.endTurnDamagePerHit <= 0 ||
                 attack.birdAttack.endTurnAreaCoefficient < 0f ||
                 float.IsNaN(attack.birdAttack.endTurnAreaCoefficient) ||
                 float.IsInfinity(attack.birdAttack.endTurnAreaCoefficient)))
                throw new InvalidOperationException(
                    $"卡牌 {data.name} 的 birdAttack 必须绑定有效的周期 Damage、" +
                    "正数鸟数和导弹参数、有限非负的回合末范围参数，每枚导弹只配置一个直击目标。");
            string patternName = attack != null ? attack.visualPattern : data.attackPattern;
            int configuredDamage = attack != null ? attack.damagePerTarget : data.baseDamage;
            if (!TryParseAttackPattern(patternName, configuredDamage, out AttackPattern attackPattern))
                throw new InvalidOperationException($"卡牌 {data.name} 使用了未知攻击模式：{patternName}。");
            string targetPolicyName = attack != null ? attack.targetPolicy : data.targetPolicy;
            if (!TryParseTargetPolicy(targetPolicyName, attackPattern, out TargetPolicy targetPolicy))
                throw new InvalidOperationException($"卡牌 {data.name} 使用了未知目标策略：{targetPolicyName}。");
            if (targetPolicy == TargetPolicy.PlayerSelected)
                throw new InvalidOperationException($"卡牌 {data.name} 使用了首发未实现的 PlayerSelected。");
            string configuredAreaMode = attack != null ? attack.areaTriggerMode : data.areaTriggerMode;
            string areaMode = string.IsNullOrWhiteSpace(configuredAreaMode) ? "PerDirectTarget" : configuredAreaMode;
            if (!Enum.TryParse(areaMode, true, out AreaTriggerMode areaTriggerMode))
                throw new InvalidOperationException($"卡牌 {data.name} 使用了未知 Area 触发模式：{data.areaTriggerMode}。");
            if (data.isWild && data.isMild)
                throw new InvalidOperationException($"卡牌 {data.name} 不能同时是 Wild 与 Mild。");

            CardOfferingAdmission admission = CardOfferingAdmissionData.CompileOrUnknown(data.offeringAdmission);
            var card = ScriptableObject.CreateInstance<CardConfig>();
            card.SetOfferingAdmission(admission);
            card.contentId = data.contentId;
            card.cardName = data.name;
            card.description = data.description;
            card.detailDescription = data.detailDescription;
            card.volumeId = data.volumeId;
            card.color = color;
            card.costType = data.isWild
                ? CostType.Wild
                : data.isMild
                    ? CostType.Mild
                    : CostType.Default;
            card.manaCost = data.manaCost;
            card.ordinaryRewardEnabled = data.ordinaryRewardEnabled;
            card.defaultRewardUnlocked = data.defaultRewardUnlocked;
            card.rewardPoolCost = data.rewardPoolCost;
            card.rewardMinPlayerLevel = data.rewardMinPlayerLevel;
            card.rewardUnlockPlayerLevel = data.rewardUnlockPlayerLevel;
            card.rewardUnlockCrawlerContentId = data.rewardUnlockCrawlerContentId;
            card.rewardUnlockCondition = data.rewardUnlockCondition?.IsConfigured == true
                ? data.rewardUnlockCondition : null;
            card.baseDamage = configuredDamage;
            card.attackPattern = attackPattern;
            card.projectileCount = attack != null
                ? Math.Max(1, attack.attackInstanceCount)
                : data.projectileCount;
            card.targetPolicy = targetPolicy;
            card.directTargetsPerHit = attack != null
                ? Math.Max(0, attack.targetsPerAttack)
                : data.directTargetsPerHit;
            bool legacyCritTag = statusEffect == StatusEffect.BaoJi;
            card.canCrit = attack != null ? attack.canCrit : data.canCrit || legacyCritTag;
            card.critChance = attack != null
                ? Mathf.Clamp01(attack.critChance)
                : legacyCritTag && data.statusChance > 0f
                ? Mathf.Clamp01(data.statusChance)
                : Mathf.Clamp01(data.critChance);
            float configuredCritMultiplier = attack != null
                ? attack.critMultiplier : data.critMultiplier;
            card.critMultiplier = configuredCritMultiplier >= 1f
                ? configuredCritMultiplier : DamageSystem.DefaultCritMultiplier;
            card.areaTriggerMode = areaTriggerMode;
            float configuredAreaCoefficient = attack != null
                ? attack.areaCoefficient : data.areaCoefficient;
            card.areaCoefficient = configuredAreaCoefficient > 0f
                ? configuredAreaCoefficient : 0.2f;
            card.armor = data.armor;
            card.heal = data.heal;
            card.manaGain = data.manaGain;
            card.statusEffect = attack != null || legacyCritTag
                ? StatusEffect.None : statusEffect;
            card.statusChance = attack != null || legacyCritTag
                ? 0f : data.statusChance;
            card.destroyOnUse = data.destroyOnUse;
            card.autoPlayOnDraw = data.autoPlayOnDraw;
            card.gemSlots = Math.Max(0, Math.Min(
                CardSealRules.MaximumSealSlots, data.gemSlots));
            card.seals = CloneSeals(data.seals);
            card.effects = BuildEffects(data, attack, attackPattern,
                targetPolicy, areaTriggerMode);
            return card;
        }

        private static List<CardEffectSpec> BuildEffects(
            CardData data, CardAttackData attack, AttackPattern attackPattern,
            TargetPolicy targetPolicy, AreaTriggerMode areaTriggerMode)
        {
            List<CardEffectSpec> effects = CompileEffects(
                data.effects,
                $"卡牌 {data.contentId ?? data.name}");
            if (attack == null) return effects;

            effects ??= new List<CardEffectSpec>();
            for (int i = effects.Count - 1; i >= 0; i--)
                if (effects[i] != null && effects[i].effectType == EffectType.Damage)
                    effects.RemoveAt(i);

            string comboModeName = string.IsNullOrWhiteSpace(attack.comboMode)
                ? nameof(ComboPatternMode.ScaleHitDamage)
                : attack.comboMode;
            if (!Enum.TryParse(comboModeName, true, out ComboPatternMode comboMode))
                throw new InvalidOperationException(
                    $"卡牌 {data.name} 使用了未知连击攻击模式：{attack.comboMode}。");
            string amountModeName = string.IsNullOrWhiteSpace(attack.amountMode)
                ? nameof(AmountPatternMode.ExtraAttackInstance)
                : attack.amountMode;
            if (!Enum.TryParse(amountModeName, true, out AmountPatternMode amountMode))
                throw new InvalidOperationException(
                    $"卡牌 {data.name} 使用了未知数量模式：{attack.amountMode}。");

            TerrainEffectSpec terrainEffect = null;
            if (attack.terrainEffect?.IsConfigured == true)
            {
                CardTerrainEffectData configured = attack.terrainEffect;
                if (!Enum.TryParse(configured.effectKind, true,
                        out TerrainEffectKind effectKind) ||
                    !Enum.TryParse(configured.triggerTiming, true,
                        out TerrainEffectTiming triggerTiming) ||
                    !Enum.TryParse(configured.damageKind, true,
                        out DamageKind damageKind) ||
                    !Enum.TryParse(configured.targetMode, true,
                        out TerrainTargetMode targetMode))
                {
                    throw new InvalidOperationException(
                        $"卡牌 {data.name} 的地形效果枚举配置无效。");
                }

                terrainEffect = new TerrainEffectSpec
                {
                    effectId = configured.effectId,
                    effectKind = effectKind,
                    triggerTiming = triggerTiming,
                    damageKind = damageKind,
                    magnitudeScale = Math.Max(0f, configured.magnitudeScale),
                    triggerCount = Math.Max(1, configured.triggerCount),
                    targetMode = targetMode,
                    baseMagnitude = configured.baseMagnitude,
                    areaCoefficient = configured.areaCoefficient,
                    persistUntilCombatEnd = configured.persistUntilCombatEnd
                };
            }

            var damageSpec = new DamageSpec
            {
                baseHitDamage = Math.Max(0, attack.damagePerTarget),
                manaDamageCoefficient = attack.manaDamageCoefficient,
                limitBreakAttackInstancesPerLevel = Math.Max(
                    0,
                    attack.limitBreakAttackInstancesPerLevel),
                pattern = new AttackPatternSpec
                {
                    patternType = attackPattern,
                    baseAttackInstanceCount = Math.Max(1, attack.attackInstanceCount),
                    targetPolicy = targetPolicy,
                    directTargetsPerSubHit = Math.Max(0, attack.targetsPerAttack),
                    comboMode = comboMode,
                    amountMode = amountMode
                },
                crit = new CritSpec
                {
                    enabled = attack.canCrit,
                    chance = Mathf.Clamp01(attack.critChance),
                    multiplier = Math.Max(1f, attack.critMultiplier),
                    areaDamageBasis = AreaDamageBasis.NormalHitDamage
                },
                area = new AreaSpec
                {
                    enabled = areaTriggerMode != AreaTriggerMode.Disabled,
                    coefficient = Math.Max(0f, attack.areaCoefficient),
                    triggerMode = areaTriggerMode
                },
                cardLocalDamageMultiplier = Math.Max(0f, attack.damageMultiplier),
                comboScalable = attack.comboScalable,
                amountScalable = attack.amountScalable,
                birdAttack = attack.birdAttack?.enabled == true
                    ? new BirdAttackSpec
                    {
                        missilesPerBird = attack.birdAttack.missilesPerBird,
                        endTurnMissilesPerBird = attack.birdAttack.endTurnMissilesPerBird,
                        endTurnDamagePerHit = attack.birdAttack.endTurnDamagePerHit,
                        endTurnAreaCoefficient = attack.birdAttack.endTurnAreaCoefficient
                    }
                    : null,
                terrainEffect = terrainEffect,
                ricochetProjectile = attack.ricochetProjectile?.enabled == true
                    ? new RicochetProjectileSpec { enabled = true,
                        minimumLifetime = attack.ricochetProjectile.minimumLifetime,
                        maximumLifetime = attack.ricochetProjectile.maximumLifetime,
                        amountSpeedCoefficient = attack.ricochetProjectile.amountSpeedCoefficient }
                    : null
            };

            if (attack.onHitEffects != null)
            {
                foreach (CardOnHitEffectData configured in attack.onHitEffects)
                {
                    if (configured == null ||
                        !Enum.TryParse(configured.effectType, true,
                            out OnHitEffectType effectType))
                        continue;
                    damageSpec.onHitEffects.Add(new OnHitEffectSpec
                    {
                        effectType = effectType,
                        baseChance = configured.overrideChance
                            ? Mathf.Clamp01(configured.chance)
                            : 0f,
                        useRuleDefaultChance = !configured.overrideChance
                    });
                }
            }

            if (attack.bounce != null && attack.bounce.enabled)
            {
                string sequenceModeName = string.IsNullOrWhiteSpace(
                        attack.bounce.sequenceMode)
                    ? nameof(BounceSequenceMode.PerDirectHit)
                    : attack.bounce.sequenceMode;
                if (!Enum.TryParse(sequenceModeName, true,
                        out BounceSequenceMode sequenceMode))
                {
                    throw new InvalidOperationException(
                        $"卡牌 {data.name} 使用了未知弹跳时序：" +
                        $"{attack.bounce.sequenceMode}。");
                }
                if (sequenceMode == BounceSequenceMode.PerDirectHit &&
                    attack.bounce.hitsPerDirectHit <= 0)
                {
                    throw new InvalidOperationException(
                        $"卡牌 {data.name} 的逐直击弹跳次数必须大于 0。");
                }

                damageSpec.bounce = new BounceSpec
                {
                    enabled = true,
                    sequenceMode = sequenceMode,
                    hitsPerDirectHit = Math.Max(
                        0,
                        attack.bounce.hitsPerDirectHit),
                    damageIncrementPerBounce = Math.Max(
                        0,
                        attack.bounce.damageIncrementPerBounce)
                };
            }

            effects.Insert(0, new CardEffectSpec
            {
                effectType = EffectType.Damage,
                target = TargetRule.PlayedTarget,
                amount = damageSpec.baseHitDamage,
                count = damageSpec.pattern.baseAttackInstanceCount,
                damageSpec = damageSpec,
                repeatAtPlayerTurnEnd = attack.repeatAtPlayerTurnEnd,
                replayColumnsAtPlayerTurnEnd = attack.replayColumnsAtPlayerTurnEnd,
                comboScalable = attack.comboScalable
            });
            return effects;
        }

        private static EnemyStats CreateEnemy(
            EnemyData data,
            Func<string, CardConfig> temporaryCardResolver = null)
        {
            if (!Enum.TryParse(data.bodySize, true, out EnemyBodySize bodySize))
                bodySize = EnemyBodySize.Medium;

            EnemyBehaviorDefinition behaviorDefinition = null;
            if (!string.IsNullOrWhiteSpace(data.behaviorProfileId) &&
                !EnemyBehaviorProfileCatalog.TryResolve(
                    data.behaviorProfileId,
                    out behaviorDefinition,
                    out string behaviorError))
            {
                throw new InvalidOperationException(
                    $"敌人 {data.contentId} 的行为配置无效：" +
                    behaviorError);
            }
            if (behaviorDefinition != null)
            {
                behaviorDefinition = behaviorDefinition
                    .BindTemporaryCardDefinitions(temporaryCardResolver);
            }

            return new EnemyStats(
                data.name,
                data.baseHealth,
                data.maxHit,
                data.armor,
                data.isBoss,
                data.contentId,
                data.gridWidth,
                data.gridDepth,
                bodySize,
                data.knockbackMultiplier,
                data.canBeDisplaced,
                data.disarmMultiplier,
                data.xp,
                data.maxArmor,
                behaviorDefinition,
                data.shortDescription);
        }

        private static List<CardSealSpec> CloneSeals(List<CardSealSpec> seals)
        {
            if (seals == null) return null;
            var result = new List<CardSealSpec>(seals.Count);
            foreach (CardSealSpec seal in seals)
                if (seal != null) result.Add(seal.Clone());
            return result;
        }

        public static bool TryParseStatusEffect(string value, out StatusEffect statusEffect)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                statusEffect = StatusEffect.None;
                return true;
            }

            return Enum.TryParse(value, true, out statusEffect)
                || LegacyStatusEffects.TryGetValue(value, out statusEffect);
        }

        private static bool TryParseAttackPattern(
            string configuredPattern, int configuredDamage,
            out AttackPattern attackPattern)
        {
            if (string.IsNullOrWhiteSpace(configuredPattern))
            {
                attackPattern = configuredDamage > 0
                    ? AttackPattern.SingleShot
                    : AttackPattern.None;
                return true;
            }

            return Enum.TryParse(configuredPattern, true, out attackPattern);
        }

        private static bool TryParseTargetPolicy(
            string configuredPolicy, AttackPattern attackPattern,
            out TargetPolicy targetPolicy)
        {
            if (!string.IsNullOrWhiteSpace(configuredPolicy))
                return Enum.TryParse(configuredPolicy, true, out targetPolicy);
            targetPolicy = attackPattern == AttackPattern.WhipSlash ||
                attackPattern == AttackPattern.MultiTarget
                    ? TargetPolicy.AllAttackable
                    : TargetPolicy.LeftToRight;
            return true;
        }

        private static void LinkEvolutions(
            IEnumerable<EvolutionData> evolutions,
            IReadOnlyDictionary<string, CardConfig> cardsById)
        {
            foreach (EvolutionData evolution in evolutions)
            {
                if (!cardsById.TryGetValue(
                        evolution.primaryCardContentId,
                        out CardConfig primary))
                {
                    throw new InvalidOperationException(
                        $"进化配方 {evolution.contentId} 缺少主卡：" +
                        $"{evolution.primaryCardContentId}。");
                }
                if (!cardsById.TryGetValue(
                        evolution.resultCardContentId,
                        out CardConfig result))
                {
                    throw new InvalidOperationException(
                        $"进化配方 {evolution.contentId} 缺少结果卡：" +
                        $"{evolution.resultCardContentId}。");
                }
                primary.evolutionTarget = result;
            }
        }

        /// <summary>
        /// DTO→运行时配置的唯一映射入口。映射 contentId/name/volumeId/
        /// playstyleTag/triggerColor/triggerEffect/triggerEffects/triggerFollowUp/duration/
        /// cardMana/enterEffect/recruitPrice/cardContentId/startingCardContentIds。
        /// </summary>
        public static CrawlerConfig CreateCrawler(CrawlerData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var crawler = ScriptableObject.CreateInstance<CrawlerConfig>();
            crawler.contentId = data.contentId;
            crawler.crawlerName = data.name;
            crawler.description = data.playstyleTag;
            crawler.volumeId = CoalesceVolumeId(data.volumeId, "common");
            crawler.triggerEffect = data.triggerEffect;
            crawler.duration = data.duration;
            crawler.leaderStats = data.leaderStats?.Clone();
            crawler.cardMana = data.cardMana;
            crawler.enterEffect = data.enterEffect;
            crawler.triggerEffects = CompileEffects(
                data.triggerEffects,
                $"英灵 {data.contentId ?? data.name}");
            crawler.triggerFollowUp = ParseTriggerFollowUp(
                data.triggerFollowUp,
                $"英灵 {data.contentId ?? data.name}");
            crawler.triggerFollowUpAmount = Math.Max(1, data.triggerFollowUpAmount);
            crawler.cardContentId = data.cardContentId;
            crawler.startingCardContentIds = data.startingCardContentIds != null
                ? data.startingCardContentIds.ToArray()
                : Array.Empty<string>();

            if (!string.IsNullOrWhiteSpace(data.triggerColor) &&
                Enum.TryParse(data.triggerColor, true, out CardColor triggerColor))
            {
                crawler.triggerColor = triggerColor;
            }
            else
            {
                crawler.triggerColor = CardColor.Yang;
            }

            return crawler;
        }

        private static CrawlerTriggerFollowUp ParseTriggerFollowUp(
            string value,
            string owner)
        {
            if (string.IsNullOrWhiteSpace(value))
                return CrawlerTriggerFollowUp.None;
            if (Enum.TryParse(
                value.Trim(),
                ignoreCase: true,
                out CrawlerTriggerFollowUp result) &&
                Enum.IsDefined(typeof(CrawlerTriggerFollowUp), result) &&
                result != CrawlerTriggerFollowUp.None)
            {
                return result;
            }

            throw new InvalidOperationException(
                $"{owner} 使用未知 triggerFollowUp：{value}。");
        }

        /// <summary>
        /// 按稳定 contentId 解析英灵配置；未找到返回 null，由调用方降级处理。
        /// Bootstrap 场景装配与模拟器共用此入口，保证英灵卡打出的解析口径一致。
        /// </summary>
        public static CrawlerConfig CreateCrawlerByContentId(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId)) return null;
            if (!ContentCatalog.Current.TryGetCrawler(contentId, out CrawlerData data))
                return null;
            var crawler = CreateCrawler(data);
            crawler.contentId = contentId;
            return crawler;
        }

        private static List<CardEffectSpec> CompileEffects(
            List<CardEffectSpec> effects,
            string owner)
        {
            if (effects == null) return null;
            var result = new List<CardEffectSpec>(effects.Count);
            for (int index = 0; index < effects.Count; index++)
            {
                CardEffectSpec source = effects[index];
                if (source == null) continue;
                if (source.repeatAtPlayerTurnEnd && source.effectType != EffectType.Damage)
                    throw new InvalidOperationException($"{owner} 的周期源必须绑定基础 Damage。");
                if (!ComboScalingAuthoring.TryResolve(
                        source,
                        out bool comboScalable))
                {
                    throw new InvalidOperationException(
                        $"{owner} 的效果[{index}] 使用未知 " +
                        $"comboScaling：{source.comboScaling}。");
                }

                if (source.replayColumnsAtPlayerTurnEnd &&
                    (source.effectType != EffectType.Damage || source.repeatAtPlayerTurnEnd ||
                     source.damageSpec?.birdAttack != null))
                    throw new InvalidOperationException("一次列补打必须绑定独立Damage，不能与周期/鸟源同时启用。");

                result.Add(new CardEffectSpec
                {
                    effectType = source.effectType,
                    target = source.target,
                    amount = source.amount,
                    count = source.count,
                    damageSpec = ContentDamageSpecCopy.Copy(source.damageSpec),
                    repeatAtPlayerTurnEnd = source.repeatAtPlayerTurnEnd,
                    replayColumnsAtPlayerTurnEnd = source.replayColumnsAtPlayerTurnEnd,
                    statusId = source.statusId,
                    chance = source.chance,
                    duration = source.duration,
                    conditions = CloneConditions(source.conditions),
                    statType = source.statType,
                    costModifier = source.costModifier,
                    randomCostMin = source.randomCostMin,
                    randomCostMax = source.randomCostMax,
                    randomCostRelative = source.randomCostRelative,
                    createCardDefinitionId =
                        source.createCardDefinitionId,
                    crawlerContentId = source.crawlerContentId,
                    attackIntentOnly = source.attackIntentOnly,
                    removeAllIntents = source.removeAllIntents,
                    // 作者三态在此消耗；运行时只携带编译后 bool。
                    comboScaling = "",
                    comboScalable = comboScalable
                });
            }
            return result;
        }

        private static List<ConditionRule> CloneConditions(
            List<ConditionRule> conditions)
        {
            if (conditions == null) return null;
            var result = new List<ConditionRule>(conditions.Count);
            foreach (ConditionRule source in conditions)
            {
                if (source == null) continue;
                result.Add(new ConditionRule
                {
                    type = source.type,
                    intValue = source.intValue,
                    floatValue = source.floatValue,
                    stringValue = source.stringValue
                });
            }
            return result;
        }

        private static string CoalesceVolumeId(string volumeId, string fallback)
        {
            return !string.IsNullOrWhiteSpace(volumeId) ? volumeId : fallback;
        }
    }
}
