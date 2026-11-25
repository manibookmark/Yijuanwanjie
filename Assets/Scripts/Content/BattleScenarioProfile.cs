using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    public enum RewardUtilityPolicy
    {
        InheritDaozangUnlocks = 0,
        Disabled = 1
    }

    /// <summary>
    /// 一场战斗的可复用输入数据。
    /// 正式场景可由流程选择卷属，组件验证场景则绑定固定 contentId；
    /// 两者都交给同一个 CombatSceneBootstrap 组装。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewBattleScenario",
        menuName = "万界长卷/战斗场景数据")]
    public sealed class BattleScenarioProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 3;

        [Header("身份")]
        public int schemaVersion = CurrentSchemaVersion;
        public string scenarioId = "battle.scenario.new";
        public string volumeId = "xiyou";

        [Header("玩家")]
        [Min(1)] public int maxHealth = 120;
        [Min(1)] public int baseMana = 12;
        [Tooltip("每回合开始的基础抽牌数；手牌持有数量没有上限，字段名保留旧配置兼容。")]
        [Min(1)] public int handLimit = 8;
        [Tooltip("旧场景迁移兼容值；不再驱动每回合标准补牌，也不等同于引卷。")]
        [Min(0)] public int turnDrawCount = 8;
        public int might;
        public int luck = 15;
        public int nirvana = 10;

        [Header("内容关联")]
        [Tooltip("按稳定 contentId 选择牌组；顺序也是测试场景的预期展示顺序。")]
        public List<string> deckContentIds = new List<string>();
        [Tooltip("按稳定 contentId 选择敌人；为空时使用卷属默认敌人。")]
        public List<string> enemyContentIds = new List<string>();
        [Tooltip("本场唯一层 BOSS／关 BOSS 的稳定 contentId。空=没有显式 BOSS；Floor 只排在队尾，Stage 等待其余敌人全灭后再从天而降。")]
        public string levelBossContentId = "";
        [Tooltip("levelBossContentId 的遭遇职责。Floor=层 BOSS，Stage=关 BOSS。")]
        public BossRank levelBossRank = BossRank.Floor;
        [Tooltip("本场预选英灵的稳定 contentId；仅用于内容与表现预加载，不会在开战时自动召唤。实际驻场由英灵卡的 SummonCrawler 效果触发。")]
        public string crawlerContentId =
            "legacy.crawler.common.5a3f3db3";
        [Tooltip("可选的固定实测参考装载。配置后首次 Run 入场忽略玩家选英灵，按该装载的牌组与基础属性复放。")]
        public string referenceLoadoutContentId = "";

        [Header("天劫计时")]
        [Tooltip("开启后启用 Boss 天劫计时器；关闭时传 null BossTimer。")]
        public bool bossTimerEnabled = true;
        [Min(1)] public int bossTimerEyes = 5;
        [Min(0)] public int bossTimerDamage = 25;

        [Header("分波敌人")]
        [Tooltip("波次大小列表（如 [5,5] 表示 5+5 分批）。空列表表示不分波。总和必须等于 enemyContentIds.Count。")]
        public List<int> enemyWaveSizes = new List<int>();

        [Header("按行布阵")]
        [Tooltip("每排怪物数量（0=最前排，如 [5,0,3] 表示第一排 5、第二排留空、第三排 3）。空列表=自动贪心布阵。总和必须等于 enemyContentIds.Count，且不能与 enemyWaveSizes 同时配置。")]
        public List<int> enemyFormationRows = new List<int>();
        [Tooltip("旧棋盘布阵图案，行从前到后以 / 分隔（如 10101/01010/11111），1=该格有怪。显式源矩阵存在时仅保留作旧资产兼容，不再是作者源。")]
        [FormerlySerializedAs("enemyFormationPattern")]
        [SerializeField] private string serializedEnemyFormationPattern = "";
        [Tooltip("显式来源矩阵的列数。配置格位后必须填写；它独立于项目战场的固定 5 列。")]
        [Min(0)] public int enemyFormationSourceColumnCount;
        [Tooltip("逐敌人、按源矩阵顺序的原始格位。列表索引与 enemyContentIds 索引一一对应，允许层 BOSS 处于中段格位。")]
        public List<EnemyFormationSourceSlot> enemyFormationSourceSlots =
            new List<EnemyFormationSourceSlot>();

        /// <summary>
        /// Existing hosts only transport a formation string to Core.  Keep the
        /// author-facing source fields as the single source of truth and expose
        /// a lossless internal transport string here, so legacy hosts continue
        /// to work without learning a second formation path.
        /// </summary>
        public string enemyFormationPattern
        {
            get => HasExplicitSourceFormation
                ? EnemyFormationLayout.EncodeSourceMatrixPattern(
                    enemyFormationSourceColumnCount,
                    enemyFormationSourceSlots)
                : serializedEnemyFormationPattern;
            set => serializedEnemyFormationPattern = value ?? string.Empty;
        }

        public bool HasExplicitSourceFormation =>
            enemyFormationSourceColumnCount > 0 &&
            enemyFormationSourceSlots != null &&
            enemyFormationSourceSlots.Count > 0;

        private bool HasAnySourceFormationField =>
            enemyFormationSourceColumnCount != 0 ||
            (enemyFormationSourceSlots != null &&
             enemyFormationSourceSlots.Count > 0);

        [Header("测试模式")]
        [Tooltip("开启后：未完成卡作为 F6 验收对象；同一牌组中已完成或未登记的卡仍可作为联动支持卡随机抽取。F7 只浏览已登记卡牌；选中 VFXTestRoot 后可在 Inspector 隐藏已确认卡。")]
        public bool testOnlyUnconfirmed = false;

        [Header("遭遇覆盖")]
        [Tooltip(">0 时覆盖本场所有敌人的血量；0 沿用敌人内容配置。")]
        public int enemyHealthOverride = 0;
        [Tooltip(">0 时覆盖本场所有敌人的经验奖励；0 沿用敌人内容配置。")]
        [Min(0)] public int enemyExperienceOverride = 0;
        [Tooltip("玩家起始护甲。0=无护甲。")]
        [Min(0)] public int startingArmor = 0;

        [Header("入场教学触发")]
        [Tooltip("战斗入场教学提示文案；非空时入场显示、首次击杀后淡出（§5.1 机制演示）。空=不显示。")]
        public string introHintText = "";
        [Tooltip("初始抽牌展示完成后一次性获得的经验。默认 0；仅供明确需要入场经验的特殊遭遇。")]
        [Min(0)] public int entryExperienceGrant = 0;
        [Tooltip("首次升级固定展示的卡牌候选；为空时使用通用随机奖励。")]
        public List<string> fixedFirstLevelUpRewardCardContentIds =
            new List<string>();
        [Tooltip("按稳定 contentId 强制进入首回合手牌（教学连击起手，按费用递增排列）。数量不能超过 handLimit。")]
        public List<string> forcedOpeningHandContentIds =
            new List<string>();

        [Header("奖励操作")]
        [Tooltip("常规战斗继承道藏解锁；试玩或特殊战斗可强制隐藏刷新、放弃、排除。")]
        public RewardUtilityPolicy rewardUtilityPolicy =
            RewardUtilityPolicy.InheritDaozangUnlocks;

        public bool HasReferenceLoadout =>
            !string.IsNullOrWhiteSpace(referenceLoadoutContentId);

        /// <summary>
        /// 解析固定实测装载。缺失是作者配置错误，必须在进入战斗前显式失败，
        /// 不能退回项目默认起手并伪装成已复放的竞品实测。
        /// </summary>
        public RunReferenceLoadoutProfile ResolveReferenceLoadout()
        {
            if (!HasReferenceLoadout)
                return null;

            RunReferenceLoadoutProfile profile =
                RunReferenceLoadoutProfileCatalog.Get(
                    referenceLoadoutContentId);
            if (profile == null)
            {
                throw new InvalidOperationException(
                    "战斗场景缺少实测参考装载：" +
                    referenceLoadoutContentId);
            }
            return profile;
        }

        public PlayerStats CreatePlayer()
        {
            // 只设置基础属性，Power-Up 属性（锋锐/天运/还魂等）由 ApplyDaozangProgression 叠加
            var player = new PlayerStats
            {
                maxHealth = Mathf.Max(1, maxHealth),
                currentHealth = Mathf.Max(1, maxHealth),
                armor = Mathf.Max(0, startingArmor),
                faLi = Mathf.Max(1, baseMana),
                shouPai = Mathf.Max(1, handLimit),
                // 只保留旧资产读取兼容；标准补牌由 shouPai 驱动。
                chouPai = Mathf.Max(0, turnDrawCount),
                // Power-Up 属性默认为 0，只有购买经诀后 ApplyDaozangProgression 才叠加
            };
            ResolveReferenceLoadout()?.ApplyTo(player);
            return player;
        }

        public List<string> ValidateProfile(ContentCatalog catalog = null)
        {
            catalog ??= ContentCatalog.Current;
            var errors = new List<string>();
            if (schemaVersion != CurrentSchemaVersion)
                errors.Add($"schemaVersion 必须为 {CurrentSchemaVersion}。");
            if (string.IsNullOrWhiteSpace(scenarioId))
                errors.Add("scenarioId 不能为空。");
            if (!catalog.TryGetVolume(volumeId, out VolumeContent volume))
            {
                errors.Add($"volumeId 不存在：{volumeId}。");
                return errors;
            }
            if (maxHealth <= 0 || baseMana <= 0 || handLimit <= 0)
                errors.Add("玩家生命、基础灵力和回合抽牌数必须大于 0。");
            if (entryExperienceGrant < 0)
                errors.Add("entryExperienceGrant 不能小于 0。");
            if (enemyExperienceOverride < 0)
                errors.Add("enemyExperienceOverride 不能小于 0。");
            if (turnDrawCount < 0 || turnDrawCount > handLimit)
                errors.Add("旧 turnDrawCount 兼容值必须在 0 到回合抽牌数之间。");
            if (bossTimerEyes <= 0 || bossTimerDamage < 0)
                errors.Add("天劫计时配置不合法。");

            var cardIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (CardData card in catalog.GetVolume("common").cards)
                cardIds.Add(card.contentId);
            foreach (CardData card in volume.cards)
                cardIds.Add(card.contentId);
            ValidateReferences(
                deckContentIds,
                cardIds,
                "卡牌",
                allowEmpty: false,
                errors);
            ValidateReferences(
                fixedFirstLevelUpRewardCardContentIds,
                cardIds,
                "首次升级奖励卡牌",
                allowEmpty: true,
                errors);

            var enemyIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var enemiesById = new Dictionary<string, EnemyData>(
                StringComparer.OrdinalIgnoreCase);
            foreach (EnemyData enemy in catalog.GetVolume("common").enemies)
            {
                enemyIds.Add(enemy.contentId);
                enemiesById[enemy.contentId] = enemy;
            }
            foreach (EnemyData enemy in volume.enemies)
            {
                enemyIds.Add(enemy.contentId);
                enemiesById[enemy.contentId] = enemy;
            }
            ValidateReferences(
                enemyContentIds,
                enemyIds,
                "敌人",
                allowEmpty: true,
                errors);

            if (!string.IsNullOrWhiteSpace(levelBossContentId))
            {
                if (!enemiesById.TryGetValue(
                        levelBossContentId,
                        out EnemyData levelBoss))
                {
                    errors.Add(
                        $"关卡 BOSS contentId 不存在：{levelBossContentId}。");
                }
                else if (!levelBoss.isBoss)
                {
                    errors.Add(
                        $"关卡 BOSS 必须配置 isBoss=true：{levelBossContentId}。");
                }
                if (levelBossRank != BossRank.Floor &&
                    levelBossRank != BossRank.Stage)
                {
                    errors.Add(
                        $"关卡 BOSS 职责必须为 Floor 或 Stage，" +
                        $"当前为 {levelBossRank}。");
                }

                int occurrenceCount = 0;
                if (enemyContentIds != null)
                {
                    for (int i = 0; i < enemyContentIds.Count; i++)
                    {
                        if (string.Equals(
                                enemyContentIds[i],
                                levelBossContentId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            occurrenceCount++;
                        }
                    }
                }
                if (occurrenceCount != 1)
                {
                    errors.Add(
                        "levelBossContentId 必须在 enemyContentIds 中且只出现一次，" +
                        $"当前出现 {occurrenceCount} 次。");
                }
            }

            // 波次大小校验
            if (enemyWaveSizes != null && enemyWaveSizes.Count > 0)
            {
                int waveTotal = 0;
                for (int i = 0; i < enemyWaveSizes.Count; i++)
                {
                    if (enemyWaveSizes[i] <= 0)
                    {
                        errors.Add($"enemyWaveSizes[{i}] 必须为正数，当前为 {enemyWaveSizes[i]}。");
                    }
                    waveTotal += enemyWaveSizes[i];
                }
                int enemyCount = enemyContentIds != null ? enemyContentIds.Count : 0;
                if (waveTotal != enemyCount)
                    errors.Add($"enemyWaveSizes 总和 ({waveTotal}) 必须等于 enemyContentIds 数量 ({enemyCount})。");
            }

            // 显式源矩阵与旧图案/按行/分波是互斥的作者入口。旧 pattern
            // 在源矩阵存在时只是迁移残留，因此不要求作者手动清空它。
            bool hasSourceFormation = HasAnySourceFormationField;
            string formationPattern = null;
            if (hasSourceFormation)
            {
                if (!HasExplicitSourceFormation)
                {
                    errors.Add(
                        "显式源矩阵必须同时配置列数和至少一个格位。");
                }
                else
                {
                    try
                    {
                        formationPattern = enemyFormationPattern;
                    }
                    catch (ArgumentException exception)
                    {
                        errors.Add("显式源矩阵不合法：" +
                            exception.Message);
                    }
                }
            }
            else
            {
                formationPattern = serializedEnemyFormationPattern;
            }
            bool hasFormationPattern =
                !string.IsNullOrWhiteSpace(formationPattern);

            // 按行布阵校验
            if (enemyFormationRows != null && enemyFormationRows.Count > 0)
            {
                if (enemyWaveSizes != null && enemyWaveSizes.Count > 0)
                    errors.Add("enemyWaveSizes 与 enemyFormationRows 不能同时配置。");
                if (hasSourceFormation)
                    errors.Add("显式源矩阵与 enemyFormationRows 不能同时配置。");
                else if (hasFormationPattern)
                    errors.Add("enemyFormationPattern 与 enemyFormationRows 不能同时配置。");

                int rowTotal = 0;
                for (int i = 0; i < enemyFormationRows.Count; i++)
                {
                    if (enemyFormationRows[i] < 0)
                        errors.Add($"enemyFormationRows[{i}] 不能为负数，当前为 {enemyFormationRows[i]}。");
                    rowTotal += Math.Max(0, enemyFormationRows[i]);
                }
                int enemyCountForRows = enemyContentIds != null ? enemyContentIds.Count : 0;
                if (rowTotal != enemyCountForRows)
                    errors.Add($"enemyFormationRows 总和 ({rowTotal}) 必须等于 enemyContentIds 数量 ({enemyCountForRows})。");
                if (enemyFormationRows.Count > EnemyFormationLayout.HiddenRowLimit)
                    errors.Add($"enemyFormationRows 排数 ({enemyFormationRows.Count}) 不能超过 {EnemyFormationLayout.HiddenRowLimit}。");
            }

            // 棋盘布阵校验
            if (hasFormationPattern)
            {
                if (enemyWaveSizes != null && enemyWaveSizes.Count > 0)
                    errors.Add(hasSourceFormation
                        ? "显式源矩阵与 enemyWaveSizes 不能同时配置。"
                        : "enemyFormationPattern 与 enemyWaveSizes 不能同时配置。");
                int enemyCountForPattern = enemyContentIds != null ? enemyContentIds.Count : 0;
                if (!EnemyFormationLayout.TryParsePatternCells(
                        formationPattern,
                        enemyCountForPattern,
                        out _,
                        out string patternError))
                {
                    errors.Add(hasSourceFormation
                        ? $"显式源矩阵不合法：{patternError}"
                        : $"enemyFormationPattern 不合法：{patternError}");
                }
            }

            // 强制起手校验
            if (forcedOpeningHandContentIds != null &&
                forcedOpeningHandContentIds.Count > 0)
            {
                ValidateReferences(
                    forcedOpeningHandContentIds,
                    cardIds,
                    "强制起手卡牌",
                    allowEmpty: true,
                    errors);
                if (forcedOpeningHandContentIds.Count > handLimit)
                    errors.Add($"强制起手卡牌数 ({forcedOpeningHandContentIds.Count}) 不能超过回合抽牌数 ({handLimit})。");
                var deckIdSet = new HashSet<string>(
                    deckContentIds ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < forcedOpeningHandContentIds.Count; i++)
                {
                    string id = forcedOpeningHandContentIds[i];
                    if (!string.IsNullOrWhiteSpace(id) && !deckIdSet.Contains(id))
                        errors.Add($"强制起手卡牌必须来自牌组（deckContentIds），当前缺失：{id}。");
                }
            }

            if (!string.IsNullOrWhiteSpace(crawlerContentId))
            {
                bool foundCrawler = false;
                foreach (VolumeContent candidate in catalog.Volumes)
                {
                    if (candidate.crawlers.Exists(
                        item => string.Equals(
                            item.contentId,
                            crawlerContentId,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        foundCrawler = true;
                        break;
                    }
                }
                if (!foundCrawler)
                    errors.Add($"英灵 contentId 不存在：{crawlerContentId}。");
            }

            return errors;
        }

        private static void ValidateReferences(
            IEnumerable<string> references,
            ISet<string> availableIds,
            string label,
            bool allowEmpty,
            ICollection<string> errors)
        {
            if (references == null)
            {
                if (!allowEmpty) errors.Add($"{label}列表不能为空。");
                return;
            }

            int count = 0;
            foreach (string contentId in references)
            {
                count++;
                if (string.IsNullOrWhiteSpace(contentId))
                    errors.Add($"{label} contentId 不能为空。");
                else if (!availableIds.Contains(contentId))
                    errors.Add($"{label} contentId 不存在：{contentId}。");
            }
            if (!allowEmpty && count == 0)
                errors.Add($"{label}列表至少需要一项。");
        }
    }
}
