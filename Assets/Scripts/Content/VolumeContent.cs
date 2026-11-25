using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// 卷属内容包：每卷独立的角色/卡牌/遗物/敌人/关卡
    /// </summary>
    [Serializable]
    public class VolumeContent
    {
        public int schemaVersion = 3;
        public string volumeId;      // xiyou / daming / xiuxian / common
        public string displayName;   // 西游卷 / 大明卷 / 修仙卷 / 通用
        public List<CrawlerData> crawlers = new List<CrawlerData>();
        public List<CardData> cards = new List<CardData>();
        public List<EnemyData> enemies = new List<EnemyData>();
        public List<RelicData> relics = new List<RelicData>();
        public List<EvolutionData> evolutions = new List<EvolutionData>();
        public List<DungeonData> dungeons = new List<DungeonData>();
        // Optional schema-5 extension. Absence stays null for old volumes;
        // configured datasets must supply their complete members explicitly.
        public List<CommonEncounterContentData> commonEncounters;
        // Optional schema-6 shared Core source wire. Absence remains null; compilation
        // preserves the complete source contract without qualifying a Run.
        public List<SideEventSourceContentData> sideEventSources;
        // Optional schema-6 extension. No source identity or member is synthesized;
        // the strict Core wire owns its recursive source DTOs. Unity serializes only
        // the established volume fields and never scans or synthesizes this extension.
        [NonSerialized]
        public List<GemSourceRegistryData> gemSources;
        // Optional schema-6 author timeline. The same closed source reader establishes
        // every field before compiling a Candidate or Enabled immutable definition.
        [NonSerialized]
        public List<DungeonSideEventTimelineContentData> sideEventTimelines;
    }

    [Serializable]
    public class CrawlerData
    {
        public string contentId;
        public string name;
        public string volumeId;
        public string triggerColor;   // Huo/Tu/Mu/Shui/Jin/Yang/Yin
        public int duration;
        public CrawlerLeaderStats leaderStats;
        public int cardMana;
        public string enterEffect;
        public string triggerEffect;
        /// <summary>
        /// 结构化触发效果。为空时 triggerEffect 仅作展示文案；
        /// 每个作者效果必须显式配置 comboScaling 三态，
        /// 由 ContentRuntimeFactory 编译为运行时 comboScalable。
        /// </summary>
        public List<CardEffectSpec> triggerEffects;
        /// <summary>
        /// 触发后续规则，例如 IncreaseOtherCrawlersDuration。
        /// 空字符串保持旧作者源兼容，按 None 处理。
        /// </summary>
        public string triggerFollowUp;
        /// <summary>触发后续每次增加的凝神；未配置时默认为 1。</summary>
        public int triggerFollowUpAmount = 1;
        public string unlockCondition;
        public int recruitPrice;
        /// <summary>英灵金卡稳定 contentId；不计入四张支援牌。</summary>
        public string cardContentId;
        /// <summary>
        /// 固定四张支援牌的稳定 contentId。重复 ID 表示重复卡牌实例；
        /// 起始牌组与普通升级奖励资格彼此独立。
        /// </summary>
        public List<string> startingCardContentIds = new List<string>();
        /// <summary>
        /// 遗留显示名字符串，仅供旧 JSON 兼容读取。新内容不得使用它建立关联。
        /// </summary>
        public string startingDeck;
        public string playstyleTag;
    }

    [Serializable]
    public class CardData
    {
        public string contentId;
        public string name;
        public string volumeId;
        public string color;
        public int manaCost;
        public bool ordinaryRewardEnabled;
        public bool defaultRewardUnlocked;
        public int rewardPoolCost = -1;
        public int rewardMinPlayerLevel = 1;
        public int rewardUnlockPlayerLevel;
        public string rewardUnlockCrawlerContentId;
        public CardUnlockCondition rewardUnlockCondition;
        public CardOfferingAdmissionData offeringAdmission;
        // Independent native identity/type evidence. It does not grant Offering permission.
        public CardSourceIdentityData sourceIdentity;
        public int baseDamage;
        public int armor;
        public int heal;
        public int manaGain;
        public string statusEffect;
        public float statusChance;
        public bool destroyOnUse;
        public bool autoPlayOnDraw;
        /// <summary>
        /// 此卡可承载的灵印数。默认 1 保持旧作者源的兼容；0 用于 VC
        /// Card_Shatter 这类明确不允许装印的遭遇临时牌。
        /// </summary>
        public int gemSlots = 1;
        public bool isWild;
        public bool isMild;
        public string description;
        public string detailDescription;
        public string attackPattern = "";
        public int projectileCount = 1;
        public string targetPolicy = "";
        public int directTargetsPerHit = 1;
        public bool canCrit;
        public float critChance = 0.25f;
        public float critMultiplier = 1.5f;
        public string areaTriggerMode = "PerDirectTarget";
        public float areaCoefficient = 0.2f;

        /// <summary>
        /// 新攻击配置入口。非空时由 ContentRuntimeFactory 编译为 DamageSpec；
        /// 上方 baseDamage/projectileCount 等字段仅保留给未迁移内容。
        /// </summary>
        public CardAttackData attack;

        /// <summary>
        /// 类型化效果列表。非空时由 CardEffectSpec 驱动结算，
        /// 为空时回退到上面的散落字段（legacy 路径）。
        /// </summary>
        public List<CardEffectSpec> effects;
        public List<CardSealSpec> seals;
    }

    /// <summary>
    /// 面向内容作者的攻击配置。基础攻击物数与每实例目标数彼此独立；
    /// Content 层负责隐藏 DamageSpec/SubHit 等运行时实现细节。
    /// </summary>
    [Serializable]
    public sealed class CardAttackData
    {
        public int damagePerTarget;
        [FormerlySerializedAs("attackCount")]
        public int attackInstanceCount = 1;
        public int targetsPerAttack = 1;
        public string targetPolicy = "LeftToRight";
        public string visualPattern = "SingleShot";
        public bool canCrit;
        public float critChance = 0.25f;
        public float critMultiplier = 1.5f;
        public string areaTriggerMode = "PerDirectTarget";
        public float areaCoefficient = 0.2f;
        public float damageMultiplier = 1f;
        public float manaDamageCoefficient;
        public bool comboScalable = true;
        public bool amountScalable = true;
        /// <summary>
        /// 基础攻击后登记本遭遇持续源。普通源冻结计划并同实例刷新；
        /// 显式启用 birdAttack 时改为每次出牌追加鸟，回合末使用当前玩家属性。
        /// </summary>
        public bool repeatAtPlayerTurnEnd;
        /// <summary>实际直击登记列，本回合末补打一次并清空；与长期周期源互斥。</summary>
        public bool replayColumnsAtPlayerTurnEnd;
        public CardBirdAttackData birdAttack;
        public string comboMode = "ScaleHitDamage";
        public string amountMode = "ExtraAttackInstance";
        /// <summary>
        /// 每层卡牌突破追加的攻击物数。0 表示此攻击不受突破影响；
        /// 普通重复攻击物，或一次按列横扫的 ScaleHitDamage 波数合同。
        /// </summary>
        public int limitBreakAttackInstancesPerLevel;
        /// <summary>弹跳时序；null 或 enabled=false 时关闭。</summary>
        public CardBounceData bounce;
        public CardTerrainEffectData terrainEffect;
        public CardRicochetProjectileData ricochetProjectile;
        public List<CardOnHitEffectData> onHitEffects = new List<CardOnHitEffectData>();

        /// <summary>
        /// JsonUtility may materialize this optional nested object even when the
        /// author JSON omitted attack. A positive per-target damage is the single
        /// opt-in marker shared by validation and runtime compilation.
        /// </summary>
        public bool IsConfigured => damagePerTarget > 0;
    }

    /// <summary>
    /// 鸟攻击的作者配置：鸟数沿用 attackInstanceCount，初击与回合末导弹分别配置。
    /// 只承载序列化数据，驻留鸟状态和缩放公式统一由 Core 拥有。
    /// </summary>
    [Serializable]
    public sealed class CardBirdAttackData
    {
        // JsonUtility 可能物化省略的可选对象；只有 enabled=true 才编译鸟规则。
        public bool enabled;
        public int missilesPerBird = 2;
        public int endTurnMissilesPerBird = 5;
        public int endTurnDamagePerHit = 8;
        public float endTurnAreaCoefficient = 0.2f;
    }

    /// <summary>弹跳配置；sequenceMode 区分逐直击、同步波次与原生逐物命中预算。</summary>
    [Serializable]
    public sealed class CardBounceData
    {
        // JsonUtility 会为 JSON 中省略的嵌套对象保留字段默认值。
        // 因此弹跳必须默认关闭；只有内容显式写入 enabled=true 才能启用。
        public bool enabled;
        public string sequenceMode = "PerDirectHit";
        public int hitsPerDirectHit;
        /// <summary>每一轮弹射相对直击增加的伤害；0 表示沿用直击伤害。</summary>
        public int damageIncrementPerBounce;
    }

    /// <summary>
    /// 每次 Direct Hit 在目标格留下的地形配置。规则和表现都以 effectId
    /// 稳定关联；同一次出牌在同格叠层，不同结算批次互不合并。
    /// </summary>
    [Serializable]
    public sealed class CardTerrainEffectData
    {
        public string effectId;
        public string effectKind = "Damage";
        public string triggerTiming = "EnemyTurnStarted";
        public string damageKind = "Burn";
        public float magnitudeScale = 1f;
        public int triggerCount = 1;
        public string targetMode = "CellSnapshot";
        public int baseMagnitude;
        public float areaCoefficient = 0.2f;
        public bool persistUntilCombatEnd;

        /// <summary>
        /// JsonUtility 可能为 JSON 中省略的可选嵌套对象生成空壳实例；
        /// 地形效果必须以稳定 effectId 显式启用，不能仅凭对象非 null 判断。
        /// </summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(effectId);
    }

    [Serializable]
    public sealed class CardRicochetProjectileData
    {
        public bool enabled;
        public float minimumLifetime = 0.5f;
        public float maximumLifetime = 1.5f;
        public float amountSpeedCoefficient = 0.1f;
    }

    [Serializable]
    public sealed class CardOnHitEffectData
    {
        public string effectType;
        /// <summary>
        /// false 时由 CombatRuleSet 按效果类型提供全局概率；只有明确的特殊卡
        /// 才设为 true 并填写 chance。
        /// </summary>
        public bool overrideChance;
        public float chance;
    }

    [Serializable]
    public class EnemyData
    {
        public string contentId;
        public string name;
        public string shortDescription;
        public string volumeId;
        public int baseHealth;
        public int xp;
        public int maxHit;
        public int armor;
        public int maxArmor;
        public bool isBoss;
        public int difficulty;     // 1-9
        public int gridWidth = 1;
        public int gridDepth = 1;
        public string bodySize = "Medium";
        public float knockbackMultiplier = -1f;
        public bool canBeDisplaced = true;
        public float disarmMultiplier = -1f;
        public string behaviorProfileId;
    }

    [Serializable]
    public class RelicData
    {
        public string contentId;
        public string name;
        public string description;
        public int relicType;
        public string unlockLocation;
    }

    [Serializable]
    public class EvolutionData
    {
        public string contentId;
        /// <summary>运行时关联使用的主卡稳定 contentId。</summary>
        public string primaryCardContentId;
        /// <summary>旧作者数据兼容字段；正式进化只读取主卡与结果卡，必须为空。</summary>
        public List<string> secondaryCardContentIds = new List<string>();
        /// <summary>进化结果卡的稳定 contentId。</summary>
        public string resultCardContentId;

        // 旧显示名字段只保留给迁移报告和策划阅读，不得再作为关联键。
        public string primaryCard;
        /// <summary>旧副卡显示名兼容字段；新作者源必须为空。</summary>
        public List<string> secondaryCards = new List<string>();
        public string resultCard;
    }

    [Serializable]
    public class DungeonData
    {
        public string contentId;
        public string name;
        public string volumeId;
        public int difficulty;
        public int floors;
        public string unlockCondition;
        public string reward;
        public bool hasBoss;
        public string bossName;
    }
}
