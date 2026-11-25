using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Meta
{
    /// <summary>
    /// 局外成长控制器
    /// 管理：Power-Up 升级、英灵招募、宝石配置、遗物、Arcana、进化、免费退款
    /// </summary>
    [Serializable]
    public class MetaProgression
    {
        /// <summary>
        /// G1 完整通关后复原道藏与群英殿所使用的稳定关卡 ID。
        /// </summary>
        public const string G1FacilityUnlockLevelContentId =
            "level.xiyou.shuangchaling.v1";

        /// <summary>
        /// G2 完整通关后的终局设施阶段使用此稳定关卡 ID。
        /// F1 的 Polentír 只开放天机玩法能力，不提前复原物理建筑。
        /// </summary>
        public const string G2FacilityUnlockLevelContentId =
            "level.xiyou.yingchoujian.v1";

        // 货币
        public int lingyu = 0;          // 灵玉（局外永久）
        public int totalLingyuEarned = 0; // 累计获得（用于长期累计条件）

        // Power-Up 等级：powerUpName → level
        public Dictionary<string, int> powerUpLevels = new Dictionary<string, int>();

        // 已解锁/激活内容
        public List<string> unlockedCrawlers = new List<string>();  // 已招募英灵
        public List<string> unlockedRelics = new List<string>();    // 已获得遗珍
        public List<RelicType> unlockedRelicTypes = new List<RelicType>();
        public List<string> unlockedArcanas = new List<string>();   // 已解锁秘法
        public List<string> unlockedCards = new List<string>();      // 已解锁卡牌
        private readonly CardUnlockProgress _cardUnlocks = new CardUnlockProgress();
        /// <summary>Permanent ordinary-card facts; current Run eligibility is projected separately.</summary>
        public CardUnlockProgress CardUnlocks => _cardUnlocks;
        private readonly EventUnlockProgress _eventUnlocks = new EventUnlockProgress();
        public EventUnlockProgress EventUnlocks => _eventUnlocks;
        private readonly ExperienceCollectionProgress _experienceCollections = new ExperienceCollectionProgress();
        public ExperienceCollectionProgress ExperienceCollections => _experienceCollections;
        public List<string> unlockedSeals =
            LingyinCatalog.CreateDefaultUnlockedSealContentIds();    // 已解锁灵印
        // 已通关关卡的稳定 contentId；Hub 关区与后续关卡用它判定解锁。
        public List<string> clearedLevelContentIds = new List<string>();
        // 已完成的存档级一次性演出 contentId；删存档后才会重新播放。
        public List<string> completedPresentations = new List<string>();

        // 当前装备
        public string equippedArcana = null;   // 装备的秘法
        public bool hasEquippedArcanaEffect;
        public ArcanaEffect equippedArcanaEffect = ArcanaEffect.Custom;
        public List<string> equippedCrawlers = new List<string>(); // 队伍英灵

        // 解锁进度
        // 序列化回执字段；设施权限只读稳定关卡／遗珍事实，不以此字段旁路解锁。
        public bool shopUnlocked = false;
        public bool tier2Unlocked = false;     // 历史回执字段；权限只读正式经诀规则
        public bool tier3Unlocked = false;     // 历史回执字段；权限只读正式经诀规则
        public bool blacksmithUnlocked = false; // 鉴宝斋解锁
        // Polentír 的取得回执；玩法能力与物理建筑分别由下方只读属性推导。
        public bool arcanasUnlocked = false;

        // 奖励候选操作由后续道藏经诀解锁；默认全部隐藏。
        public bool rewardRefreshUnlocked = false;
        public bool rewardAbandonUnlocked = false;
        public bool rewardExcludeUnlocked = false;

        // 事件
        public event Action<string> OnLog;

        /// <summary>
        /// G1 设施阶段只由正式完整通关记录复原。
        /// </summary>
        public bool IsG1FacilityStageUnlocked =>
            IsLevelCleared(G1FacilityUnlockLevelContentId);

        /// <summary>
        /// 道藏功能与 G1 设施阶段共用同一解锁条件。
        /// </summary>
        public bool IsDaozangUnlocked => IsG1FacilityStageUnlocked;

        /// <summary>
        /// F1 Polentír 的 Arcane Tent 能力只认实际取得的稳定遗珍 ID。
        /// </summary>
        public bool IsFortuneTellerArcanaTentUnlocked =>
            IsRelicUnlocked(RelicContentIds.Polentir);

        /// <summary>
        /// G2 终局物理设施阶段：必须同时拥有完整关卡通关记录和终局观星台。
        /// 不存 S3 布尔值，也不读取 F1 Arcane Tent 的能力标志。
        /// </summary>
        public bool IsG2FacilityStageUnlocked =>
            IsLevelCleared(G2FacilityUnlockLevelContentId) &&
            IsRelicUnlocked(RelicContentIds.GuanxingTai);

        /// <summary>万宝阁入口、名册和建筑共用终局设施条件。</summary>
        public bool IsWanbaogeUnlocked => IsG2FacilityStageUnlocked;

        /// <summary>天机阁建筑随 G2 终局复原；不以 F1 玩法能力提前点亮。</summary>
        public bool IsTianjiBuildingUnlocked => IsG2FacilityStageUnlocked;

        /// <summary>出发前选择与规则准入所用的天机玩法能力；不表示物理建筑已复原。</summary>
        public bool IsArcanaFacilityUnlocked => IsFortuneTellerArcanaTentUnlocked;

        // ========== 灵玉 ==========

        /// <summary>
        /// 记入已经由 EconomySystem 完成敛玉计算的最终灵玉，不再二次放大。
        /// </summary>
        public void CreditLingyu(int finalAmount)
        {
            int actual = Mathf.Max(0, finalAmount);
            if (actual <= 0) return;
            lingyu += actual;
            totalLingyuEarned += actual;
        }

        // ========== Power-Up ==========

        public int GetPowerUpLevel(PowerUpConfig config)
        {
            if (config == null || powerUpLevels == null) return 0;
            if (!string.IsNullOrWhiteSpace(config.StableId) &&
                powerUpLevels.TryGetValue(config.StableId, out int stableLevel))
                return stableLevel;
            return !string.IsNullOrWhiteSpace(config.powerUpName) &&
                   powerUpLevels.TryGetValue(config.powerUpName, out int legacyLevel)
                ? legacyLevel
                : 0;
        }

        public bool BuyPowerUp(PowerUpConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.StableId))
            {
                OnLog?.Invoke("✗ 经诀缺少稳定 contentId");
                return false;
            }
            if (!IsDaozangUnlocked)
            {
                OnLog?.Invoke("✗ 道藏未解锁");
                return false;
            }

            if (DaozangProgressionRules.IsUpgradeLocked(this, config.StableId))
            {
                OnLog?.Invoke("✗ 经诀尚未开放");
                return false;
            }

            // 当前等级
            string key = config.StableId;
            // 旧存档只会在这个迁移适配器中读显示名称；新写入统一使用 contentId。
            if (!string.IsNullOrWhiteSpace(config.powerUpName) && config.powerUpName != key &&
                powerUpLevels.TryGetValue(config.powerUpName, out int legacyLevel))
            {
                powerUpLevels.Remove(config.powerUpName);
                int stableLevel = powerUpLevels.TryGetValue(key, out int existingLevel) ? existingLevel : 0;
                powerUpLevels[key] = Math.Max(stableLevel, legacyLevel);
            }
            if (!powerUpLevels.ContainsKey(key))
                powerUpLevels[key] = 0;

            int level = powerUpLevels[key];
            if (level >= config.maxLevel) { OnLog?.Invoke($"✗ {config.powerUpName} 已满级"); return false; }

            int price = config.GetLevelPrice(level + 1);
            if (lingyu < price) { OnLog?.Invoke($"✗ 灵玉不足！需要 {price}，当前 {lingyu}"); return false; }

            lingyu -= price;
            powerUpLevels[key] = level + 1;
            OnLog?.Invoke($"▶ {config.powerUpName} Lv{level + 1}/{config.maxLevel} | -{price} 灵玉 | 剩余 {lingyu}");
            return true;
        }

        /// <summary>
        /// 免费全额退款
        /// </summary>
        public void RefundAll(List<PowerUpConfig> allConfigs)
        {
            int totalRefund = 0;
            foreach (var kvp in powerUpLevels)
            {
                foreach (var cfg in allConfigs)
                {
                    if (cfg != null && (cfg.StableId == kvp.Key || cfg.powerUpName == kvp.Key))
                    {
                        for (int level = 1; level <= kvp.Value; level++)
                            totalRefund += cfg.GetLevelPrice(level);
                        break;
                    }
                }
            }
            powerUpLevels.Clear();
            lingyu += totalRefund;
            OnLog?.Invoke($"★ 全额退款！+{totalRefund} 灵玉 | 当前 {lingyu}");
        }

        public int GetStatAmount(StatType stat)
        {
            int total = 0;
            foreach (var kvp in powerUpLevels)
            {
                // 简化：按名称匹配
                if (kvp.Key.Contains(stat.ToString()))
                {
                    total += kvp.Value;
                }
            }
            return total;
        }

        // ========== 英灵招募 ==========

        /// <summary>按稳定 contentId 幂等解锁英灵。</summary>
        public bool TryUnlockCrawler(string crawlerContentId)
        {
            if (string.IsNullOrWhiteSpace(crawlerContentId))
                throw new ArgumentException("crawlerContentId 不能为空。", nameof(crawlerContentId));

            // Check if already unlocked (by contentId or legacy name)
            if (unlockedCrawlers.Contains(crawlerContentId))
                return false;

            unlockedCrawlers.Add(crawlerContentId);
            OnLog?.Invoke($"★ 解锁英灵 [{crawlerContentId}]");
            return true;
        }

        /// <summary>按稳定 contentId 检查英灵是否已解锁。</summary>
        public bool IsCrawlerUnlocked(string crawlerContentId)
        {
            if (string.IsNullOrWhiteSpace(crawlerContentId))
                return false;
            return unlockedCrawlers.Contains(crawlerContentId);
        }

        public bool RecruitCrawler(CrawlerConfig crawler)
        {
            string stableId = !string.IsNullOrWhiteSpace(crawler.contentId)
                ? crawler.contentId
                : crawler.crawlerName;

            // Already recruited by stable ID
            if (unlockedCrawlers.Contains(stableId))
            {
                OnLog?.Invoke($"✗ {crawler.crawlerName} 已招募");
                return false;
            }

            // Legacy: list might contain old display name — normalize it
            if (!string.IsNullOrWhiteSpace(crawler.crawlerName) &&
                unlockedCrawlers.Contains(crawler.crawlerName))
            {
                // Replace display name with stable ID in-place
                unlockedCrawlers.Remove(crawler.crawlerName);
                unlockedCrawlers.Add(stableId);
                OnLog?.Invoke($"★ 英灵 [{crawler.crawlerName}] 已归一化为稳定 ID");
                return false; // already recruited, just normalized
            }

            // Price calculation
            int price = crawler.recruitPrice + unlockedCrawlers.Count * 10;
            if (lingyu < price)
            {
                OnLog?.Invoke($"✗ 灵玉不足！需要 {price}，当前 {lingyu}");
                return false;
            }

            lingyu -= price;
            unlockedCrawlers.Add(stableId);
            OnLog?.Invoke($"★ 招募英灵 [{crawler.crawlerName}] | -{price} 灵玉 | 剩余 {lingyu}");
            return true;
        }

        // ========== 遗珍（内部 Relic） ==========

        public void UnlockRelic(RelicConfig relic)
        {
            if (relic == null || string.IsNullOrWhiteSpace(relic.contentId))
            {
                OnLog?.Invoke("✗ 遗珍缺少稳定 contentId");
                return;
            }

            TryUnlockRelic(
                relic.contentId,
                relic.type,
                relic.relicName,
                relic.description);
        }

        /// <summary>按稳定 contentId 幂等获得遗珍，并迁移早期九叠占位 ID。</summary>
        public bool TryUnlockRelic(
            string relicContentId,
            RelicType type,
            string displayName,
            string description)
        {
            if (string.IsNullOrWhiteSpace(relicContentId))
                throw new ArgumentException(
                    "relicContentId 不能为空。", nameof(relicContentId));

            unlockedRelics ??= new List<string>();
            unlockedRelicTypes ??= new List<RelicType>();
            bool changed = false;

            if (string.Equals(
                    relicContentId,
                    RelicContentIds.Jiudie,
                    StringComparison.Ordinal) &&
                unlockedRelics.Remove(RelicContentIds.LegacyComboSwitch))
            {
                changed = true;
                OnLog?.Invoke("★ 已将旧版连击遗物归一化为九叠");
            }

            if (!unlockedRelics.Contains(relicContentId))
            {
                unlockedRelics.Add(relicContentId);
                changed = true;
            }
            if (!unlockedRelicTypes.Contains(type))
            {
                unlockedRelicTypes.Add(type);
                changed = true;
            }

            ApplyRelicFacilityUnlock(relicContentId, type);
            if (changed)
            {
                OnLog?.Invoke(
                    $"★ 获得遗珍 [{displayName ?? relicContentId}] | " +
                    (description ?? string.Empty));
            }
            return changed;
        }

        public bool IsRelicUnlocked(string relicContentId)
        {
            if (string.IsNullOrWhiteSpace(relicContentId) ||
                unlockedRelics == null)
                return false;
            if (unlockedRelics.Contains(relicContentId))
                return true;
            return string.Equals(
                       relicContentId,
                       RelicContentIds.Jiudie,
                       StringComparison.Ordinal) &&
                   unlockedRelics.Contains(
                       RelicContentIds.LegacyComboSwitch);
        }

        public bool HasRelicType(RelicType type)
            => unlockedRelicTypes != null &&
               unlockedRelicTypes.Contains(type);

        private void ApplyRelicFacilityUnlock(
            string relicContentId,
            RelicType type)
        {
            switch (type)
            {
                case RelicType.StardustAnvil:
                    blacksmithUnlocked = true;
                    break;
                case RelicType.Polentir:
                    arcanasUnlocked = true;
                    if (string.Equals(
                            relicContentId,
                            RelicContentIds.Polentir,
                            StringComparison.Ordinal))
                    {
                        // VC 中 Polentír 同时开放 Fortune Teller，且自动赠送
                        // Experimental Medicine 与 Shield Bash。用稳定 ID 记录，
                        // 不依赖仍未加载的 UI 或 ArcanaConfig 实例。
                        TryUnlockArcana(
                            ArcanaContentIds.ExperimentalMedicine,
                            "枯木逢春",
                            "治疗效果翻倍。获得 Polentír 时自动解锁。");
                        TryUnlockArcana(
                            ArcanaContentIds.ShieldBash,
                            "化甲为锋",
                            "打出护甲卡后，以当前总护甲攻击敌人前排。" +
                            "获得 Polentír 时自动解锁。");
                    }
                    break;
            }
        }

        // ========== 秘法 ==========

        public bool UnlockArcana(ArcanaConfig arcana)
        {
            if (arcana == null || string.IsNullOrWhiteSpace(arcana.contentId))
            {
                OnLog?.Invoke("✗ 秘法缺少稳定 contentId");
                return false;
            }
            return TryUnlockArcana(
                arcana.contentId,
                arcana.arcanaName,
                arcana.description);
        }

        /// <summary>
        /// 按稳定 ID 幂等解锁天机。用于内容资产尚未加载时已有明确定义的
        /// 自动解锁合同（例如 Polentír 的两项 VC 起始天机）；调用方不得以
        /// 显示名称作为 ID。
        /// </summary>
        public bool TryUnlockArcana(
            string arcanaContentId,
            string displayName = null,
            string description = null)
        {
            if (string.IsNullOrWhiteSpace(arcanaContentId))
            {
                OnLog?.Invoke("✗ 天机缺少稳定 contentId");
                return false;
            }
            unlockedArcanas ??= new List<string>();
            if (unlockedArcanas.Contains(arcanaContentId))
            {
                OnLog?.Invoke($"✗ {displayName ?? arcanaContentId} 已解锁");
                return false;
            }
            unlockedArcanas.Add(arcanaContentId);
            OnLog?.Invoke(
                $"★ 解锁秘法 [{displayName ?? arcanaContentId}] | " +
                (description ?? string.Empty));
            return true;
        }

        /// <summary>按稳定 contentId 检查天机是否已解锁。</summary>
        public bool IsArcanaUnlocked(string arcanaContentId)
        {
            return !string.IsNullOrWhiteSpace(arcanaContentId) &&
                   unlockedArcanas != null &&
                   unlockedArcanas.Contains(arcanaContentId);
        }

        /// <summary>Queries permanent ordinary-card eligibility by stable ID.</summary>
        public bool IsCardUnlocked(string cardContentId)
        {
            if (string.IsNullOrWhiteSpace(cardContentId) || unlockedCards == null)
                return false;
            string id = cardContentId.Trim();
            return unlockedCards.Exists(value => string.Equals(
                value?.Trim(), id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Idempotently grants ordinary-card eligibility. This does not grant a
        /// Run card or announce durable success; the enclosing transaction owns saving.
        /// </summary>
        public bool TryUnlockCard(string cardContentId)
        {
            if (string.IsNullOrWhiteSpace(cardContentId))
                throw new ArgumentException("cardContentId 不能为空。", nameof(cardContentId));
            string id = cardContentId.Trim();
            if (IsCardUnlocked(id)) return false;
            unlockedCards ??= new List<string>();
            unlockedCards.Add(id);
            return true;
        }

        /// <summary>按稳定 contentId 检查普通灵印是否已解锁。</summary>
        public bool IsSealUnlocked(string sealContentId)
        {
            return !string.IsNullOrWhiteSpace(sealContentId) &&
                   unlockedSeals != null &&
                   unlockedSeals.Contains(sealContentId);
        }

        /// <summary>按稳定 contentId 幂等解锁普通灵印。</summary>
        public bool TryUnlockSeal(string sealContentId)
        {
            if (string.IsNullOrWhiteSpace(sealContentId))
                throw new ArgumentException(
                    "sealContentId 不能为空。",
                    nameof(sealContentId));

            unlockedSeals ??= new List<string>();
            if (unlockedSeals.Contains(sealContentId))
                return false;
            unlockedSeals.Add(sealContentId);
            OnLog?.Invoke($"★ 解锁灵印 [{sealContentId}]");
            return true;
        }

        public bool EquipArcana(string arcanaName)
        {
            if (!unlockedArcanas.Contains(arcanaName))
            {
                OnLog?.Invoke($"✗ {arcanaName} 未解锁");
                return false;
            }
            equippedArcana = arcanaName;
            OnLog?.Invoke($"▶ 装备秘法 [{arcanaName}]");
            return true;
        }

        public bool EquipArcana(ArcanaConfig arcana)
        {
            if (arcana == null || string.IsNullOrWhiteSpace(arcana.contentId))
            {
                OnLog?.Invoke("✗ 秘法缺少稳定 contentId");
                return false;
            }
            if (!unlockedArcanas.Contains(arcana.contentId))
            {
                OnLog?.Invoke($"✗ {arcana.arcanaName} 未解锁");
                return false;
            }
            equippedArcana = arcana.contentId;
            equippedArcanaEffect = arcana.effect;
            hasEquippedArcanaEffect = true;
            OnLog?.Invoke($"▶ 装备秘法 [{arcana.arcanaName}]");
            return true;
        }

        // ========== 关卡通关记录 ==========

        /// <summary>按稳定 contentId 幂等记录关卡通关。</summary>
        public bool TryRecordClearedLevel(string levelContentId)
        {
            if (string.IsNullOrWhiteSpace(levelContentId))
                throw new ArgumentException(
                    "levelContentId 不能为空。", nameof(levelContentId));

            clearedLevelContentIds ??= new List<string>();
            if (clearedLevelContentIds.Contains(levelContentId))
                return false;

            clearedLevelContentIds.Add(levelContentId);
            OnLog?.Invoke($"★ 关卡通关 [{levelContentId}]");
            if (string.Equals(
                    levelContentId,
                    G1FacilityUnlockLevelContentId,
                    StringComparison.Ordinal) &&
                !shopUnlocked)
            {
                shopUnlocked = true;
                OnLog?.Invoke("★ 道藏与群英殿已复原！");
            }
            return true;
        }

        /// <summary>按稳定 contentId 检查关卡是否已通关。</summary>
        public bool IsLevelCleared(string levelContentId)
        {
            if (string.IsNullOrWhiteSpace(levelContentId) ||
                clearedLevelContentIds == null)
                return false;
            return clearedLevelContentIds.Contains(levelContentId);
        }

        /// <summary>按稳定 contentId 检查存档级一次性演出是否已完成。</summary>
        public bool IsPresentationCompleted(string presentationContentId)
        {
            if (string.IsNullOrWhiteSpace(presentationContentId) ||
                completedPresentations == null)
                return false;
            return completedPresentations.Contains(presentationContentId);
        }

        /// <summary>幂等记录已完成的存档级演出；返回是否首次记录。</summary>
        public bool TryCompletePresentation(string presentationContentId)
        {
            if (string.IsNullOrWhiteSpace(presentationContentId))
                throw new ArgumentException(
                    "presentationContentId 不能为空。", nameof(presentationContentId));

            completedPresentations ??= new List<string>();
            if (completedPresentations.Contains(presentationContentId))
                return false;

            completedPresentations.Add(presentationContentId);
            return true;
        }

        // ========== Tier 解锁 ==========

        public void UnlockTier2()
        {
            OnLog?.Invoke("二阶经诀将在后续第一桥关开放，当前版本尚未开放。");
        }

        public void UnlockTier3()
        {
            OnLog?.Invoke("三阶经诀将在后续第二桥关开放，当前版本尚未开放。");
        }

        // ========== 存档 ==========

        public string GetSummary()
        {
            int totalLevels = 0;
            foreach (var kvp in powerUpLevels) totalLevels += kvp.Value;
            return $"灵玉:{lingyu} | PowerUp:{totalLevels}级 | 英灵:{unlockedCrawlers.Count} | " +
                   $"遗物:{unlockedRelics.Count} | 秘法:{unlockedArcanas.Count} | " +
                   $"Tier2:{!DaozangProgressionRules.IsLocked(this, UnlockTier.Tier2)} " +
                   $"Tier3:{!DaozangProgressionRules.IsLocked(this, UnlockTier.Tier3)} | 道藏:{IsDaozangUnlocked}";
        }
    }
}
