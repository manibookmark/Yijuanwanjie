using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>关卡节点类型。本轮只有 Encounter/Finale 能进入战斗。</summary>
    public enum LevelNodeType
    {
        Start,
        Encounter,
        Finale,
        Interaction,
        [Obsolete("Use Interaction. The numeric value is retained for serialized assets.")]
        Reward = Interaction,
        Exit = 4
    }

    /// <summary>地图内非战斗交互完成时可配置的效果类型。</summary>
        public enum LevelInteractionEffectKind
    {
        AddBaseMana,
        GrantJade,
        GrantRelic,
        GrantSeal,
        HealCurrentHealth,
        ConvertCard,
        /// <summary>为后续战斗增加本 Run 的锋锐百分点。</summary>
        AddMight,
        /// <summary>
        /// 记录一条稳定剧情线索。已保存的类型化回执就是权威事实，
        /// 不再另存一份容易漂移的布尔标记。
        /// </summary>
        RecordStoryClue,
        GrantCard,
        GrantExperience,
        RefineCard,
        ChooseSeal,
        /// <summary>
        /// 将当前 Run 推进到一项实测参考结果的最低等级；不使用虚构的原始 XP。
        /// 新值只追加在枚举尾部，保留既有资产序列化数值。
        /// </summary>
        ReachLevel,
        /// <summary>
        /// 为本次 Dungeon 后续战斗增加每回合标准自动抽牌数量；
        /// 当前战斗不即时抽牌。枚举名保留 AddHandLimit 以兼容既有序列化资产。
        /// </summary>
        AddHandLimit,
        /// <summary>已展示但尚未接入运行时效果的节点；执行时应显示为不可用。</summary>
        Unavailable,
        /// <summary>从作者配置的稳定卡牌 contentId 候选中选择并获得一张。</summary>
        ChooseCard,
        /// <summary>为后续战斗增加本 Run 的裂空百分点。</summary>
        AddArea,
        /// <summary>增加本 Run 后续奖励三选一的可用重选次数。</summary>
        AddRewardReroll,
        /// <summary>增加本 Run 后续战斗每回合的基础护甲。</summary>
        AddBaseArmor,
        /// <summary>Choose or skip one lawful in-run Arcana; account equipment stays unchanged.</summary>
        ChooseArcana,
        /// <summary>Consume one source-admitted exact Run card for three Floor Chicken, atomically completing its node.</summary>
        ChickenOffering,
        /// <summary>Three explicit accepted collisions release one separately collected map Mana pickup.</summary>
        ManaStatue,
        ManaOffering,
        /// <summary>Four accepted collisions clear the cart; its bracer remains a separate pickup.</summary>
        AbandonedCart,
        /// <summary>Explicit whole-Run Crystal throwing lifecycle; never an automatic reward.</summary>
        CrystalThrowing,
        /// <summary>Independent Gem reward lifecycle; only an explicit resolved offer can open it.</summary>
        GemChest,
        /// <summary>Exact-card attribute offering with its independent durable journal.</summary>
        CardStatOffering,
        /// <summary>Duplicate one explicitly admitted persistent instance without consuming it.</summary>
        DuplicateOffering,
        /// <summary>One explicitly admitted Run FCC hire or its frozen flat compensation.</summary>
        GunForHire,
        /// <summary>Free project invitation, independent of the native paid Hire contract.</summary>
        CrawlerInvitation
    }

    /// <summary>单项地图交互完成效果。effectId 在同一关卡内必须稳定且唯一。</summary>
    [Serializable]
    public sealed class LevelInteractionEffect
    {
        public string effectId;
        public LevelInteractionEffectKind kind;
        public int amount;
        [Tooltip("随机发放上限；0 = 固定 amount，> amount 时每次首次结算在 [amount, amountMax] 内随机")]
        public int amountMax;
        public string targetContentId;
        [Tooltip("仅 ChooseCard：按作者顺序展示的固定卡牌 contentId 候选。")]
        public List<string> cardChoices = new List<string>();
        public string presentationId;
        [Tooltip("获得框粒子品阶；不改变底框、图标或文字模板")]
        public RewardRarity rarity = RewardRarity.Common;
        [Tooltip("奖励名称；用于获得标题后缀与 tooltip 标题")]
        public string summaryText;
        [Tooltip("悬停奖励本体时在 tooltip 中显示的完整规则说明")]
        public string detailText;
        [Tooltip("固定灵印候选，按稳定 contentId 提交；不从随机池抽取。")]
        public List<CardSealSpec> sealChoices = new List<CardSealSpec>();
        [Tooltip("随机选印时使用的版本化灵印池 ID；与固定 sealChoices 互斥。")]
        public string sealRewardPoolId;
        [Tooltip("固定三选一的化玉基础值；0 表示不提供化玉。")]
        public int cashOutJade;
        [Tooltip("RefineCard 可选的精确固定演化分支；留空保持通用精炼菜单。")]
        public LevelInteractionFixedEvolutionContract fixedEvolution;
        [Tooltip("ReachLevel 的实测参考装载与目标等级合同；其他交互类型必须留空。")]
        public LevelInteractionTargetLevelContract targetLevelResult;
        [Tooltip("ChooseSeal 可选双钥支付：先原子移除两张指定本色的持久非英灵牌，再选印或化玉。")]
        public LevelInteractionDoubleKeyContract doubleKeyPayment;
        [Tooltip("ChooseArcana 的版本化池与候选数；其他效果必须留空。") ]
        public LevelInteractionArcanaChoiceContract arcanaChoice;
        public bool HasArcanaChoiceContract => arcanaChoice != null && arcanaChoice.IsSpecified;
        public LevelInteractionDuplicateOfferingContract duplicateOffering;
        public bool HasDuplicateOfferingContract => duplicateOffering != null && duplicateOffering.IsSpecified;
        public LevelInteractionInvitationContract crawlerInvitation;
        public bool HasCrawlerInvitationContract => crawlerInvitation != null && crawlerInvitation.IsSpecified;
        public LevelInteractionGunForHireContract gunForHire;
        public bool HasGunForHireContract => gunForHire != null && gunForHire.IsSpecified;
        public LevelInteractionCardStatOfferingContract cardStatOffering;
        public bool HasCardStatOfferingContract => cardStatOffering != null && cardStatOffering.IsSpecified;
        public LevelInteractionChickenOfferingContract chickenOffering;
        public bool HasChickenOfferingContract => chickenOffering != null && chickenOffering.IsSpecified;
        public LevelInteractionManaOfferingContract manaOffering;
        public bool HasManaOfferingContract => manaOffering != null && manaOffering.IsSpecified;
        public LevelInteractionManaStatueContract manaStatue;
        public bool HasManaStatueContract => manaStatue != null && manaStatue.IsSpecified;
        public LevelInteractionCrystalContract crystalThrowing;
        public bool HasCrystalThrowingContract => crystalThrowing != null && crystalThrowing.IsSpecified;
        public LevelInteractionAbandonedCartContract abandonedCart;
        public bool HasAbandonedCartContract => abandonedCart != null && abandonedCart.IsSpecified;

        /// <summary>
        /// Unity serializes a null nested plain class as an empty shell in
        /// some existing assets.  Treat such a shell as absent so adding these
        /// optional contracts never changes legacy interactions after an asset
        /// save/reload round trip.
        /// </summary>
        public bool HasFixedEvolutionContract =>
            fixedEvolution != null && fixedEvolution.IsSpecified;

        /// <summary>See <see cref="HasFixedEvolutionContract"/>.</summary>
        public bool HasTargetLevelResultContract =>
            targetLevelResult != null && targetLevelResult.IsSpecified;

        public bool HasDoubleKeyPayment =>
            doubleKeyPayment != null && doubleKeyPayment.IsSpecified;
    }

    /// <summary>地图交互效果的只读结算回执。</summary>
    public readonly struct LevelInteractionReceipt
    {
        public string EffectId { get; }
        public LevelInteractionEffectKind Kind { get; }
        public string TargetContentId { get; }
        public string PresentationId { get; }
        public string SummaryText { get; }
        public string DetailText { get; }
        public RewardRarity Rarity { get; }
        public int Amount { get; }
        public int ValueBefore { get; }
        public int ValueAfter { get; }
        public bool NewlyApplied { get; }
        /// <summary>
        /// 需要精确运行时对象的效果所提交的目标实例。0 表示该效果没有
        /// 运行时目标（例如化为灵玉或普通地图奖励）。
        /// </summary>
        public int TargetRuntimeInstanceId { get; }

        public LevelInteractionReceipt(
            string effectId,
            LevelInteractionEffectKind kind,
            string targetContentId,
            string presentationId,
            int amount,
            int valueBefore,
            int valueAfter,
            bool newlyApplied,
            string summaryText = null,
            string detailText = null,
            RewardRarity rarity = RewardRarity.Common,
            int targetRuntimeInstanceId = 0)
        {
            EffectId = effectId;
            Kind = kind;
            TargetContentId = targetContentId;
            PresentationId = presentationId;
            SummaryText = summaryText;
            DetailText = detailText;
            Rarity = rarity;
            Amount = amount;
            ValueBefore = valueBefore;
            ValueAfter = valueAfter;
            NewlyApplied = newlyApplied;
            TargetRuntimeInstanceId = Math.Max(0, targetRuntimeInstanceId);
        }
    }

    /// <summary>Versioned in-run Arcana pool. A Unity empty optional shell is absent.</summary>
    [Serializable]
    public sealed class LevelInteractionArcanaChoiceContract
    {
        public string poolContentId;
        // Explicit author value; do not default to 3 in every legacy asset shell.
        public int candidateCount;
        public bool IsSpecified => !string.IsNullOrWhiteSpace(poolContentId) || candidateCount != 0;
    }

    /// <summary>当前 Run 对后续战斗施加的只读数值修正。</summary>
    public readonly struct LevelRunModifierSnapshot
    {
        public int BaseManaBonus { get; }
        public int BaseArmorPerTurnBonus { get; }
        public int MightBonus { get; }
        public int HandLimitBonus { get; }
        public int AreaBonus { get; }

        public LevelRunModifierSnapshot(
            int baseManaBonus,
            int baseArmorPerTurnBonus = 0,
            int mightBonus = 0,
            int handLimitBonus = 0,
            int areaBonus = 0)
        {
            BaseManaBonus = Math.Max(0, baseManaBonus);
            BaseArmorPerTurnBonus = Math.Max(0, baseArmorPerTurnBonus);
            MightBonus = Math.Max(0, mightBonus);
            HandLimitBonus = Math.Max(0, handLimitBonus);
            AreaBonus = Math.Max(0, areaBonus);
        }
    }

    /// <summary>Explicit canonical Offering identity. Empty Unity inline shells are unspecified.</summary>
    [Serializable]
    public sealed class LevelInteractionChickenOfferingContract
    {
        public string sourceEventId;
        public bool IsSpecified => !string.IsNullOrEmpty(sourceEventId);
    }

    [Serializable]
    public sealed class LevelInteractionManaOfferingContract
    {
        public string sourceEventId;
        public bool IsSpecified => !string.IsNullOrEmpty(sourceEventId);
    }

    /// <summary>
    /// 战斗胜利后由节点显式触发的直接奖励类型。
    /// 是否为 Boss 不会自动产生此类奖励。
    /// </summary>
    public enum LevelRewardKind
    {
        CrawlerUnlock = 0,
        SealChoice = 1,
        RelicUnlock = 2
    }

    /// <summary>类型化胜利奖励定义。</summary>
    [Serializable]
    public sealed class LevelRewardGrant
    {
        public string contentId;
        public LevelRewardKind kind;
        [Tooltip(
            "旧单印 SealChoice 的稳定灵印 contentId；多印候选改用 " +
            "sealChoiceContentIds。其他奖励类型继续使用此字段作为目标。")]
        public string targetContentId;
        [Tooltip(
            "仅 SealChoice：固定灵印候选，按稳定 contentId 的作者顺序展示。" +
            "为空时兼容旧 targetContentId 单印定义。")]
        public List<string> sealChoiceContentIds = new List<string>();
        [Tooltip(
            "仅 SealChoice：版本化随机灵印池 ID；与 targetContentId 和 " +
            "sealChoiceContentIds 互斥。候选在胜利提交时冻结进楼层存档。")]
        public string sealRewardPoolId;
        [Tooltip(
            "仅随机 SealChoice：基础候选数量。0 表示固定候选奖励；" +
            "随机池必须大于 0。")]
        [Min(0)] public int sealChoiceCount;
        [Tooltip(
            "仅随机 SealChoice：受天运影响的候选数量上限；必须不小于 " +
            "sealChoiceCount。")]
        [Min(0)] public int sealChoiceCountMax;
        [Tooltip(
            "仅 SealChoice：是否允许在选择具体灵印后化为灵玉。" +
            "默认保留旧奖励的可化玉行为；VC F1 Boss 的三印奖励关闭此项。")]
        public bool allowCashOut = true;
        [Tooltip(
            "可选奖励表现稳定 contentId；CrawlerUnlock 使用角色演出，" +
            "SealChoice 使用对应灵印奖励表现；规则结算不依赖表现配置")]
        public string presentationId;
    }

    /// <summary>只读奖励回执。</summary>
    public readonly struct LevelRewardReceipt
    {
        public string GrantContentId { get; }
        public LevelRewardKind Kind { get; }
        public string TargetContentId { get; }
        public string DisplayName { get; }
        public bool NewlyGranted { get; }
        public string PresentationId { get; }

        public LevelRewardReceipt(
            string grantContentId,
            LevelRewardKind kind,
            string targetContentId,
            string displayName,
            bool newlyGranted,
            string presentationId = null)
        {
            GrantContentId = grantContentId;
            Kind = kind;
            TargetContentId = targetContentId;
            DisplayName = displayName;
            NewlyGranted = newlyGranted;
            PresentationId = presentationId;
        }
    }

    /// <summary>节点进度。Resolving 禁止出现在配置快照中。</summary>
    public enum LevelNodeProgress
    {
        Locked,
        Available,
        Resolving,
        Cleared
    }

    /// <summary>表现层读取的节点视觉状态。</summary>
    public enum LevelNodeViewState
    {
        Hidden,
        Locked,
        Available,
        Completing,
        Completed
    }

    /// <summary>
    /// Explicit generated ordinary-encounter binding. A completely empty Unity
    /// serialization shell retains the old authored/preset mode; any supplied
    /// field opts into this contract and must pass complete validation.
    /// </summary>
    [Serializable]
    public sealed class LevelCommonEncounterBinding
    {
        public string datasetContentId;
        public string difficultyContentId;
        public bool hasTileDifficulty;
        public float tileDifficulty;

        public bool IsSpecified => !string.IsNullOrEmpty(datasetContentId) ||
            !string.IsNullOrEmpty(difficultyContentId) || hasTileDifficulty ||
            tileDifficulty != 0f || float.IsNaN(tileDifficulty) || float.IsInfinity(tileDifficulty);

        internal void Validate(LevelNode node, List<string> errors)
        {
            if (!IsSpecified) return;
            string prefix = "节点 " + node.contentId + " 的 commonEncounter：";
            if (node.nodeType != LevelNodeType.Encounter ||
                node.encounterRole != LevelEncounterRole.Ordinary)
                errors.Add(prefix + "只允许显式 Ordinary Encounter 节点。");
            if (!IsEncounterId(datasetContentId))
                errors.Add(prefix + "datasetContentId 必须为规范 encounter 稳定 ID。");
            if (!IsEncounterId(difficultyContentId))
                errors.Add(prefix + "difficultyContentId 必须为规范 encounter 稳定 ID。");
            if (!hasTileDifficulty || float.IsNaN(tileDifficulty) || float.IsInfinity(tileDifficulty))
                errors.Add(prefix + "必须显式填写有限 tileDifficulty，不从楼层或节点序号推断。");
        }

        private static bool IsEncounterId(string value)
        {
            string[] segments = value?.Split('.');
            if (segments == null || segments.Length < 3 || segments[0] != "encounter") return false;
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0 || (i < 2 && (segment[0] < 'a' || segment[0] > 'z')))
                    return false;
                foreach (char character in segment)
                    if (!((character >= 'a' && character <= 'z') ||
                        (character >= '0' && character <= '9') || character == '_' || character == '-'))
                        return false;
            }
            return true;
        }
    }

    /// <summary>关卡节点定义。</summary>
    [Serializable]
    public class LevelNode
    {
        [Tooltip("稳定节点标识")]
        public string contentId;
        public LevelNodeType nodeType;
        [Tooltip("探索守卫资格使用显式战斗角色；Unspecified 保留旧配置，不推测为普通战。")]
        public LevelEncounterRole encounterRole;
        [Tooltip("Encounter/Finale 节点关联的战斗场景 ID")]
        public string scenarioId;
        [Tooltip("可选的 Ordinary Encounter 生成合同；必须显式指定数据集、难度与 tile 难度。")]
        public LevelCommonEncounterBinding commonEncounter;
        public bool HasCommonEncounter => commonEncounter != null && commonEncounter.IsSpecified;
        [Tooltip("锁定本节点的唯一前置节点；同一个前置节点可被多个后续节点引用")]
        public List<string> prerequisiteNodeIds = new List<string>();
        [Tooltip(
            "仅 Encounter/Finale 可配置：战斗胜利后立即进入结算；" +
            "Boss 身份本身不会自动发奖。")]
        public List<LevelRewardGrant> victoryRewards =
            new List<LevelRewardGrant>();
        [Tooltip(
            "仅 Interaction/Exit 可配置：玩家主动执行地图交互后结算。")]
        public List<LevelInteractionEffect> completionEffects =
            new List<LevelInteractionEffect>();
        [Tooltip("交互奖励反馈全部关闭后播放的可选角色演出稳定 contentId")]
        public string completionPresentationId;
        [Tooltip("角色演出使用的可选内容变体稳定 ID，例如遗物 contentId")]
        public string completionPresentationVariantId;
    }

    /// <summary>快照中的单个节点状态。</summary>
    [Serializable]
    public class LevelNodeState
    {
        public string nodeContentId;
        public LevelNodeProgress progress;
    }

    /// <summary>关卡快照。禁止包含 Resolving。</summary>
    [Serializable]
    public class LevelSnapshot
    {
        public string snapshotId;
        public List<LevelNodeState> nodeStates = new List<LevelNodeState>();
    }

    /// <summary>
    /// 关卡运行配置。定义节点、前置关系和快照。
    /// Scale review: this remains the serialized authoring contract and validation entry.
    /// Exploration eligibility/graph rules live in LevelExplorationGuardRule; seeded
    /// mappings belong to DungeonRunExplorationState, never to this shared asset.
    /// Generated bindings add only explicit author metadata and shape validation;
    /// selection, curve evaluation and frozen rosters belong to their runtime owners.
    /// Reassess this boundary before adding any independent runtime lifecycle.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewLevelRun",
        menuName = "万界长卷/关卡运行配置")]
    public sealed class LevelRunProfile : ScriptableObject
    {
        [Tooltip("稳定配置标识")]
        public string contentId;
        [Tooltip("所属完整地牢/关卡的稳定 contentId；为空时沿用本配置 contentId。")]
        public string dungeonContentId;
        [Tooltip(
            "整座 Dungeon 通关时独立发放的基础灵玉。只在首层根配置填写；" +
            "不受敛玉影响，也不并入关内收集灵玉。")]
        [Min(0)] public int dungeonBonusJade;
        [Tooltip(
            "含 Finale 的终层通关时独立发放的本层基础灵玉。" +
            "与首层根配置的 Dungeon Bonus 分账，不受敛玉影响，" +
            "也不并入关内收集灵玉；使用 Exit 的楼层沿用 Exit 奖励。")]
        [Min(0)] public int levelBonusBaseJade;
        [Tooltip("楼层序号；0 表示旧配置未显式分层。")]
        [Min(0)] public int floorIndex;
        [Tooltip("完成 Exit 后进入的下一层稳定 contentId；终层留空。")]
        public string nextFloorContentId;
        [Tooltip("终局胜利结算后可直接开始的正式 Run 入口 ID；新关重新建立牌组与局内成长，非同 Run 换层。")]
        public string victoryNextRunEntryContentId;
        [Tooltip("前置关卡 contentId；为空表示无前置。通关该关卡前本关在关卡选择中锁定。")]
        public string prerequisiteLevelContentId;
        [Tooltip("关卡显示名；不参与任何稳定关联")]
        public string displayName;
        [Tooltip("关联的地图表现配置 contentId")]
        public string mapPresentationContentId;
        [Tooltip("首次进入地图时播放的可选角色演出稳定 contentId")]
        public string entryPresentationId;
        [Tooltip("首次进图角色演出使用的可选内容变体稳定 ID")]
        public string entryPresentationVariantId;
        [Tooltip("首次进图演出播放期间是否隐藏全部地图节点（教学关聚焦卷灵对话时使用）。")]
        public bool hideMapNodesDuringEntryPresentation;
        [Tooltip("通过 contentId 启动关卡时使用的默认快照；为空时取第一条快照。")]
        public string defaultSnapshotId;
        [Tooltip("本关跨战斗共享的等级经验规则；显式列表未覆盖的等级按配置的续算模式计算。")]
        public LevelExperienceRules levelExperienceRules =
            new LevelExperienceRules();
        [Tooltip("迁移兼容字段：仅允许留空或全局普通奖励ID；普通奖励始终使用全局规则，不由关卡选择。")]
        public string levelUpRewardRuleProfileId;
        [Tooltip("可选的固定教学升级 Offer 排程稳定 contentId；未命中时统一使用全局普通奖励规则。")]
        public string levelUpOfferScheduleProfileId;
        public List<LevelNode> nodes = new List<LevelNode>();
        public List<LevelSnapshot> snapshots = new List<LevelSnapshot>();
        [Tooltip("可选的 Run 随机探索守卫组；奖励静态前置留空，实际映射由 Run 冻结并保存。")]
        public List<LevelExplorationGuardRule> explorationGuards = new List<LevelExplorationGuardRule>();
        [Tooltip("0沿用Scenario作者阵型；1由Run独立选择并保存均衡/前压/后置/两翼模板。")]
        public int commonFormationRevision;

        public LevelNode FindNode(string nodeContentId)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].contentId == nodeContentId)
                    return nodes[i];
            return null;
        }

        public LevelSnapshot FindSnapshot(string snapshotId)
        {
            for (int i = 0; i < snapshots.Count; i++)
                if (snapshots[i].snapshotId == snapshotId)
                    return snapshots[i];
            return null;
        }

        public string ResolveDefaultSnapshotId()
        {
            if (!string.IsNullOrWhiteSpace(defaultSnapshotId))
                return defaultSnapshotId;
            return snapshots != null && snapshots.Count > 0
                ? snapshots[0].snapshotId
                : null;
        }

        /// <summary>
        /// 校验配置完整性。
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var effectIds = new HashSet<string>();
            var rewardGrantIds = new HashSet<string>();
            bool hasExitNode = false;

            if (nodes == null || nodes.Exists(node => node == null))
            {
                errors.Add("nodes 不能为空或包含空节点。");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(contentId))
                errors.Add("contentId 不能为空。");
            else if (prerequisiteLevelContentId == contentId)
                errors.Add("prerequisiteLevelContentId 不能自引用。");
            if (floorIndex < 0)
                errors.Add("floorIndex 不能小于 0。");
            DungeonRunFormationState.ValidateProfile(this, errors);
            if (dungeonBonusJade < 0)
                errors.Add("dungeonBonusJade 不能小于 0。");
            if (dungeonBonusJade > 0 && floorIndex > 1)
            {
                errors.Add(
                    "dungeonBonusJade 只能配置在 Dungeon 首层根配置中。");
            }
            if (levelBonusBaseJade < 0)
                errors.Add("levelBonusBaseJade 不能小于 0。");
            if (levelBonusBaseJade > 0 &&
                !nodes.Exists(node => node.nodeType == LevelNodeType.Finale))
                errors.Add("levelBonusBaseJade 必须配置在含 Finale 的楼层中。");
            if (!string.IsNullOrWhiteSpace(nextFloorContentId) &&
                string.Equals(nextFloorContentId, contentId,
                    StringComparison.OrdinalIgnoreCase))
                errors.Add("nextFloorContentId 不能自引用。");
            if (levelExperienceRules == null)
            {
                errors.Add("levelExperienceRules 不能为空。");
            }
            else
            {
                if (levelExperienceRules.baseRequirement <= 0)
                    errors.Add("经验曲线 baseRequirement 必须大于 0。");
                if (levelExperienceRules.curveExponent <= 0f)
                    errors.Add("经验曲线 curveExponent 必须大于 0。");
                if (!Enum.IsDefined(
                        typeof(LevelExperienceContinuationMode),
                        levelExperienceRules.continuationMode))
                {
                    errors.Add("经验曲线 continuationMode 无效。");
                }
                else if (levelExperienceRules.continuationMode ==
                    LevelExperienceContinuationMode.LinearFromExplicitTail)
                {
                    if (levelExperienceRules.explicitRequirements == null ||
                        levelExperienceRules.explicitRequirements.Count == 0)
                    {
                        errors.Add("线性经验续算必须配置显式经验需求前缀。");
                    }
                    if (levelExperienceRules.continuationIncrement <= 0)
                    {
                        errors.Add("线性经验续算 continuationIncrement 必须大于 0。");
                    }
                }
                if (levelExperienceRules.explicitRequirements != null)
                {
                    for (int i = 0;
                         i < levelExperienceRules.explicitRequirements.Count;
                         i++)
                    {
                        if (levelExperienceRules.explicitRequirements[i] <= 0)
                        {
                            errors.Add(
                                $"显式经验需求[{i}] 必须大于 0。");
                        }
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(defaultSnapshotId) &&
                FindSnapshot(defaultSnapshotId) == null)
            {
                errors.Add(
                    $"defaultSnapshotId 引用不存在的快照：{defaultSnapshotId}。");
            }
            if (string.IsNullOrWhiteSpace(entryPresentationId) &&
                !string.IsNullOrWhiteSpace(entryPresentationVariantId))
            {
                errors.Add(
                    "配置了首次进图角色演出内容变体，但缺少 entryPresentationId。");
            }
            // 节点唯一性
            var nodeIds = new HashSet<string>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (string.IsNullOrWhiteSpace(node.contentId))
                {
                    errors.Add($"节点 {i} 的 contentId 为空。");
                    continue;
                }
                if (!nodeIds.Add(node.contentId))
                    errors.Add($"节点 contentId 重复：{node.contentId}。");
                node.commonEncounter?.Validate(node, errors);
                if (!Enum.IsDefined(typeof(LevelEncounterRole), node.encounterRole) ||
                    (node.encounterRole != LevelEncounterRole.Unspecified &&
                     node.nodeType != LevelNodeType.Encounter && node.nodeType != LevelNodeType.Finale))
                    errors.Add($"节点 {node.contentId} 的 encounterRole 无效或配置在非战斗节点。");
                if (node.nodeType == LevelNodeType.Exit)
                    hasExitNode = true;

                if (string.IsNullOrWhiteSpace(
                        node.completionPresentationId) &&
                    !string.IsNullOrWhiteSpace(
                        node.completionPresentationVariantId))
                {
                    errors.Add(
                        $"节点 {node.contentId} 配置了角色演出内容变体，" +
                        "但缺少 completionPresentationId。");
                }

                // Encounter/Finale 必须有 scenarioId
                if ((node.nodeType == LevelNodeType.Encounter ||
                     node.nodeType == LevelNodeType.Finale) &&
                    string.IsNullOrWhiteSpace(node.scenarioId))
                {
                    errors.Add($"遭遇/终局节点缺少 scenarioId：{node.contentId}。");
                }

                if (node.nodeType == LevelNodeType.Interaction ||
                    node.nodeType == LevelNodeType.Exit)
                {
                    if (node.completionEffects == null ||
                        node.completionEffects.Count == 0)
                    {
                        if (node.nodeType == LevelNodeType.Interaction)
                        {
                            errors.Add(
                                $"交互节点 {node.contentId} 至少需要一个 completionEffect。");
                        }
                    }
                    else
                    {
                        for (int effectIndex = 0;
                             effectIndex < node.completionEffects.Count;
                             effectIndex++)
                        {
                            LevelInteractionEffect effect =
                                node.completionEffects[effectIndex];
                            if (effect == null)
                            {
                                errors.Add(
                                    $"交互节点 {node.contentId} 包含空 completionEffect。");
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(effect.effectId))
                            {
                                errors.Add(
                                    $"交互节点 {node.contentId} 的 effectId 不能为空。");
                            }
                            else if (!effectIds.Add(effect.effectId))
                            {
                                errors.Add(
                                    $"交互效果 ID 重复：{effect.effectId}。");
                            }

                            if (!Enum.IsDefined(
                                    typeof(RewardRarity), effect.rarity))
                            {
                                errors.Add(
                                    $"交互效果 {effect.effectId} 的奖励品阶无效。");
                            }

                            if (effect.HasDuplicateOfferingContract && effect.kind != LevelInteractionEffectKind.DuplicateOffering)
                                errors.Add("Duplicate Offering payload belongs only to its independent lifecycle.");
                            if (effect.HasCrawlerInvitationContract && effect.kind != LevelInteractionEffectKind.CrawlerInvitation)
                                errors.Add("Invitation contract belongs only to CrawlerInvitation.");
                            if (effect.HasGunForHireContract && effect.kind != LevelInteractionEffectKind.GunForHire)
                                errors.Add("Hire payload belongs only to its independent lifecycle.");
                            if (effect.HasCardStatOfferingContract && effect.kind != LevelInteractionEffectKind.CardStatOffering)
                                errors.Add("CardStat Offering payload belongs only to its independent lifecycle.");
                            if (effect.HasManaOfferingContract && effect.kind != LevelInteractionEffectKind.ManaOffering)
                                errors.Add($"Interaction {effect.effectId} uses Mana Offering data on another effect kind.");
                            if (effect.HasManaStatueContract && effect.kind != LevelInteractionEffectKind.ManaStatue)
                                errors.Add($"Interaction {effect.effectId} uses Mana statue data on another effect kind.");
                            if (effect.HasCrystalThrowingContract && effect.kind != LevelInteractionEffectKind.CrystalThrowing)
                                errors.Add("Crystal payload belongs only to its explicit lifecycle.");
                            if (effect.HasAbandonedCartContract && effect.kind != LevelInteractionEffectKind.AbandonedCart)
                                errors.Add($"Interaction {effect.effectId} uses abandoned-cart data on another effect kind.");
                            if (effect.HasChickenOfferingContract && effect.kind != LevelInteractionEffectKind.ChickenOffering)
                                errors.Add($"交互效果 {effect.effectId} 只有 ChickenOffering 可配置供奉合同。 ");
                            if (effect.HasArcanaChoiceContract && effect.kind != LevelInteractionEffectKind.ChooseArcana)
                                errors.Add($"交互效果 {effect.effectId} 只有 ChooseArcana 可配置天机池。");
                            if (effect.HasDoubleKeyPayment &&
                                effect.kind != LevelInteractionEffectKind.ChooseSeal)
                                errors.Add($"交互效果 {effect.effectId} 只有 ChooseSeal 可配置双钥支付。");

                            switch (effect.kind)
                            {
                                case LevelInteractionEffectKind.DuplicateOffering:
                                    DungeonRunDuplicateOfferings.ValidateEffect(effect, errors);
                                    if (node.completionEffects.Count != 1) errors.Add("Duplicate Offering requires one canonical completion effect.");
                                    break;
                                case LevelInteractionEffectKind.CrawlerInvitation:
                                    DungeonRunInvitations.ValidateEffect(effect, errors);
                                    if (node.completionEffects.Count != 1) errors.Add("Invitation requires one canonical completion effect.");
                                    break;
                                case LevelInteractionEffectKind.GunForHire:
                                    DungeonRunGunForHire.ValidateEffect(effect, errors);
                                    if (node.completionEffects.Count != 1) errors.Add("Hire requires one canonical completion effect.");
                                    break;
                                case LevelInteractionEffectKind.CardStatOffering:
                                    DungeonRunCardStatOfferings.ValidateEffect(effect, errors);
                                    if (node.completionEffects.Count != 1) errors.Add("CardStat Offering requires one canonical completion effect.");
                                    break;
                                case LevelInteractionEffectKind.CrystalThrowing:
                                    CrystalEventContracts.ValidateEffect(effect, errors);
                                    if (node.completionEffects.Count != 1) errors.Add("Crystal lifecycle must be the node sole completion effect.");
                                    break;
                                case LevelInteractionEffectKind.AbandonedCart:
                                    DungeonRunAbandonedCarts.ValidateEffect(effect, errors);
                                    if (node.completionEffects.Count != 1) errors.Add($"Abandoned cart {node.contentId} requires one canonical effect.");
                                    break;
                                case LevelInteractionEffectKind.ManaStatue:
                                    DungeonRunManaStatues.ValidateEffect(effect, errors);
                                    if (node.completionEffects.Count != 1) errors.Add($"Mana statue {node.contentId} requires one canonical effect.");
                                    break;
                                case LevelInteractionEffectKind.ManaOffering:
                                case LevelInteractionEffectKind.ChickenOffering:
                                    DungeonRunChickenOffering.ValidateEffect(effect, errors);
                                    if (node.completionEffects.Count != 1)
                                        errors.Add($"供奉节点 {node.contentId} 必须只配置一个完成效果。 ");
                                    break;
                                case LevelInteractionEffectKind.ChooseArcana:
                                    DungeonRunArcanas.ValidateEffect(effect, errors);
                                    if (node.completionEffects.Count != 1)
                                        errors.Add($"天机节点 {node.contentId} 必须只配置一个完成效果。");
                                    break;
                                case LevelInteractionEffectKind.GrantCard:
                                case LevelInteractionEffectKind.GrantExperience:
                                case LevelInteractionEffectKind.RefineCard:
                                case LevelInteractionEffectKind.GemChest:
                                case LevelInteractionEffectKind.ChooseSeal:
                                case LevelInteractionEffectKind.ReachLevel:
                                case LevelInteractionEffectKind.ChooseCard:
                                    DungeonRunInteractionTransaction.ValidateEffect(effect, errors);
                                    if (node.completionEffects.Count != 1)
                                        errors.Add($"交互事务节点 {node.contentId} 必须只配置一个完成效果。");
                                    break;
                                case LevelInteractionEffectKind.AddBaseMana:
                                case LevelInteractionEffectKind.AddBaseArmor:
                                case LevelInteractionEffectKind.AddHandLimit:
                                case LevelInteractionEffectKind.GrantJade:
                                case LevelInteractionEffectKind.GrantRelic:
                                case LevelInteractionEffectKind.GrantSeal:
                                case LevelInteractionEffectKind.HealCurrentHealth:
                                case LevelInteractionEffectKind.ConvertCard:
                                case LevelInteractionEffectKind.AddMight:
                                case LevelInteractionEffectKind.AddArea:
                                case LevelInteractionEffectKind.AddRewardReroll:
                                case LevelInteractionEffectKind.RecordStoryClue:
                                    if (effect.amount <= 0)
                                    {
                                        errors.Add(
                                            $"交互效果 {effect.effectId} 的 amount 必须大于 0。");
                                    }
                                    if (effect.amountMax < 0 ||
                                        (effect.amountMax != 0 &&
                                         effect.amountMax < effect.amount))
                                    {
                                        errors.Add(
                                            $"交互效果 {effect.effectId} 的 amountMax 必须为 0 或不小于 amount。");
                                    }
                                    if ((effect.kind ==
                                         LevelInteractionEffectKind.GrantRelic ||
                                         effect.kind ==
                                         LevelInteractionEffectKind.GrantSeal ||
                                         effect.kind ==
                                         LevelInteractionEffectKind.ConvertCard ||
                                         effect.kind ==
                                         LevelInteractionEffectKind.RecordStoryClue) &&
                                        string.IsNullOrWhiteSpace(
                                            effect.targetContentId))
                                    {
                                        errors.Add(
                                            $"交互效果 {effect.effectId} 必须配置稳定 targetContentId。");
                                    }
                                    if ((effect.kind ==
                                         LevelInteractionEffectKind.AddBaseMana ||
                                         effect.kind ==
                                         LevelInteractionEffectKind.AddBaseArmor ||
                                         effect.kind ==
                                         LevelInteractionEffectKind.AddHandLimit ||
                                         effect.kind ==
                                         LevelInteractionEffectKind.AddMight ||
                                         effect.kind ==
                                         LevelInteractionEffectKind.AddArea ||
                                         effect.kind ==
                                         LevelInteractionEffectKind.AddRewardReroll ||
                                         effect.kind ==
                                         LevelInteractionEffectKind.RecordStoryClue) &&
                                        effect.amountMax != 0)
                                    {
                                        errors.Add(
                                            $"Run 内效果 {effect.effectId} 必须使用固定 amount，不能配置 amountMax。");
                                    }
                                    if (effect.kind ==
                                            LevelInteractionEffectKind.RecordStoryClue &&
                                        effect.amount != 1)
                                    {
                                        errors.Add(
                                            $"剧情线索效果 {effect.effectId} 的 amount 必须为 1。");
                                    }
                                    break;
                                case LevelInteractionEffectKind.Unavailable:
                                    // 保留节点与展示合同，但不伪造运行时奖励。
                                    break;
                                default:
                                    errors.Add(
                                        $"交互效果 {effect.effectId} 的 kind 未实现：{effect.kind}。");
                                    break;
                            }
                        }
                        if (node.completionEffects.Count > 1 &&
                            node.completionEffects.Exists(effect =>
                                effect != null && effect.kind ==
                                LevelInteractionEffectKind.ConvertCard))
                        {
                            errors.Add(
                                $"归元节点 {node.contentId} 只能配置一个 " +
                                "ConvertCard completionEffect。");
                        }
                    }
                }
                else if (node.completionEffects != null &&
                         node.completionEffects.Count > 0)
                {
                    errors.Add(
                        $"非交互节点 {node.contentId} 不能配置 completionEffects。");
                }

                // 前置引用必须存在
                if (node.prerequisiteNodeIds != null)
                {
                    if (node.prerequisiteNodeIds.Count > 1)
                    {
                        errors.Add(
                            $"节点 {node.contentId} 最多只能配置一个前置节点；" +
                            "同一个前置节点可以锁定多个后续节点。");
                    }
                    foreach (var prereqId in node.prerequisiteNodeIds)
                    {
                        if (prereqId == node.contentId)
                            errors.Add($"节点 {node.contentId} 自引用前置。");
                        else if (FindNode(prereqId) == null)
                            errors.Add($"节点 {node.contentId} 的前置 {prereqId} 不存在。");
                    }
                }

                // 直接胜利奖励只能由战斗节点显式配置；Boss 标签本身不推导奖励。
                bool isCombatNode =
                    node.nodeType == LevelNodeType.Encounter ||
                    node.nodeType == LevelNodeType.Finale;
                if (!isCombatNode &&
                    node.victoryRewards != null &&
                    node.victoryRewards.Count > 0)
                {
                    errors.Add(
                        $"非战斗节点 {node.contentId} 不能配置 victoryRewards；" +
                        "地图奖励必须使用 completionEffects 交互结算。");
                }

                if (node.victoryRewards != null)
                {
                    int sealChoiceCount = 0;
                    foreach (var grant in node.victoryRewards)
                    {
                        if (grant == null)
                        {
                            errors.Add(
                                $"节点 {node.contentId} 包含空 victoryReward。");
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(grant.contentId))
                        {
                            errors.Add($"节点 {node.contentId} 有空奖励 contentId。");
                        }
                        else if (!rewardGrantIds.Add(grant.contentId))
                        {
                            errors.Add(
                                $"楼层内胜利奖励 ID 重复：{grant.contentId}。");
                        }
                        if (!Enum.IsDefined(
                                typeof(LevelRewardKind), grant.kind))
                            errors.Add(
                                $"节点 {node.contentId} 奖励 {grant.contentId} " +
                                $"使用了未知 kind：{grant.kind}。");
                        if (grant.kind == LevelRewardKind.SealChoice)
                        {
                            sealChoiceCount++;
                            bool hasRandomPool = !string.IsNullOrWhiteSpace(
                                grant.sealRewardPoolId);
                            bool hasFixedChoices =
                                grant.sealChoiceContentIds != null &&
                                grant.sealChoiceContentIds.Count > 0;
                            if (hasRandomPool)
                            {
                                if (hasFixedChoices ||
                                    !string.IsNullOrWhiteSpace(
                                        grant.targetContentId))
                                {
                                    errors.Add(
                                        $"节点 {node.contentId} 奖励 {grant.contentId} " +
                                        "随机灵印池不能同时配置固定候选或 targetContentId。");
                                }
                                if (grant.sealChoiceCount <= 0 ||
                                    grant.sealChoiceCountMax <
                                    grant.sealChoiceCount)
                                {
                                    errors.Add(
                                        $"节点 {node.contentId} 奖励 {grant.contentId} " +
                                        "随机候选数量范围无效。");
                                }
                            }
                            else if (hasFixedChoices)
                            {
                                if (grant.sealChoiceCount != 0 ||
                                    grant.sealChoiceCountMax != 0)
                                {
                                    errors.Add(
                                        $"节点 {node.contentId} 奖励 {grant.contentId} " +
                                        "固定灵印候选不能配置随机数量。");
                                }
                                if (!string.IsNullOrWhiteSpace(
                                        grant.targetContentId))
                                {
                                    errors.Add(
                                        $"节点 {node.contentId} 奖励 {grant.contentId} " +
                                        "不能同时配置 targetContentId 与多印候选。");
                                }
                                var sealIds = new HashSet<string>(
                                    StringComparer.Ordinal);
                                for (int sealIndex = 0;
                                     sealIndex <
                                     grant.sealChoiceContentIds.Count;
                                     sealIndex++)
                                {
                                    string sealId = grant
                                        .sealChoiceContentIds[sealIndex];
                                    if (string.IsNullOrWhiteSpace(sealId))
                                    {
                                        errors.Add(
                                            $"节点 {node.contentId} 奖励 " +
                                            $"{grant.contentId} 包含空灵印候选。");
                                    }
                                    else if (!sealIds.Add(sealId))
                                    {
                                        errors.Add(
                                            $"节点 {node.contentId} 奖励 " +
                                            $"{grant.contentId} 的灵印候选重复：" +
                                            $"{sealId}。");
                                    }
                                }
                            }
                            else if (string.IsNullOrWhiteSpace(
                                         grant.targetContentId))
                            {
                                errors.Add(
                                    $"节点 {node.contentId} 奖励 " +
                                    $"{grant.contentId} 缺少 targetContentId " +
                                    "或 sealChoiceContentIds。");
                            }
                            else if (grant.sealChoiceCount != 0 ||
                                     grant.sealChoiceCountMax != 0)
                            {
                                errors.Add(
                                    $"节点 {node.contentId} 奖励 {grant.contentId} " +
                                    "固定单印奖励不能配置随机数量。");
                            }
                        }
                        else if (grant.sealChoiceContentIds != null &&
                                 grant.sealChoiceContentIds.Count > 0)
                        {
                            errors.Add(
                                $"节点 {node.contentId} 奖励 {grant.contentId} " +
                                "只有 SealChoice 可配置灵印候选。");
                        }
                        else if (!string.IsNullOrWhiteSpace(
                                     grant.sealRewardPoolId) ||
                                 grant.sealChoiceCount != 0 ||
                                 grant.sealChoiceCountMax != 0)
                        {
                            errors.Add(
                                $"节点 {node.contentId} 奖励 {grant.contentId} " +
                                "只有 SealChoice 可配置随机灵印池或候选数量。");
                        }
                        else if (string.IsNullOrWhiteSpace(
                                     grant.targetContentId))
                        {
                            errors.Add($"节点 {node.contentId} 奖励 {grant.contentId} 缺少 targetContentId。");
                        }
                    }
                    // A single interactive seal transaction may precede the
                    // automatic account grants in the same victory settlement.
                    // Two seal choices would require two independent pending
                    // transactions, which this node contract cannot represent.
                    if (sealChoiceCount > 1)
                    {
                        errors.Add(
                            $"节点 {node.contentId} 最多只能配置一个 SealChoice 胜利奖励。");
                    }
                }
            }

            if (hasExitNode && string.IsNullOrWhiteSpace(nextFloorContentId))
                errors.Add("包含 Exit 节点的楼层必须配置 nextFloorContentId。");
            if (!string.IsNullOrWhiteSpace(victoryNextRunEntryContentId) &&
                (hasExitNode || !nodes.Exists(node => node != null && node.nodeType == LevelNodeType.Finale)))
                errors.Add("下一关正式入口只能配置在含 Finale 且不含 Exit 的终层。");

            // 环检测
            DetectCycles(errors);
            errors.AddRange(LevelExplorationGuardRule.Validate(this));

            // 快照校验
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snap = snapshots[i];
                if (string.IsNullOrWhiteSpace(snap.snapshotId))
                {
                    errors.Add($"快照 {i} 的 snapshotId 为空。");
                    continue;
                }

                var coveredNodes = new HashSet<string>();

                for (int j = 0; j < snap.nodeStates.Count; j++)
                {
                    var state = snap.nodeStates[j];
                    if (string.IsNullOrWhiteSpace(state.nodeContentId))
                    {
                        errors.Add($"快照 {snap.snapshotId} 的节点状态 {j} 的 nodeContentId 为空。");
                        continue;
                    }

                    if (FindNode(state.nodeContentId) == null)
                    {
                        errors.Add($"快照 {snap.snapshotId} 引用不存在的节点：{state.nodeContentId}。");
                        continue;
                    }

                    if (!coveredNodes.Add(state.nodeContentId))
                        errors.Add($"快照 {snap.snapshotId} 对节点 {state.nodeContentId} 提供了多条状态。");

                    if (state.progress == LevelNodeProgress.Resolving)
                        errors.Add($"快照 {snap.snapshotId} 禁止包含 Resolving 状态：{state.nodeContentId}。");

                }

                // 每个快照必须覆盖所有节点
                foreach (var node in nodes)
                {
                    if (!string.IsNullOrWhiteSpace(node.contentId) &&
                        !coveredNodes.Contains(node.contentId))
                        errors.Add($"快照 {snap.snapshotId} 缺少节点状态：{node.contentId}。");
                }

            }

            return errors;
        }

        private void DetectCycles(List<string> errors)
        {
            var visited = new HashSet<string>();
            var stack = new HashSet<string>();

            foreach (var node in nodes)
            {
                if (node.contentId == null) continue;
                if (visited.Contains(node.contentId)) continue;
                if (HasCycle(node.contentId, visited, stack, errors))
                    break; // report first cycle only
            }
        }

        private bool HasCycle(
            string nodeId,
            HashSet<string> visited,
            HashSet<string> stack,
            List<string> errors)
        {
            visited.Add(nodeId);
            stack.Add(nodeId);

            var node = FindNode(nodeId);
            if (node?.prerequisiteNodeIds != null)
            {
                foreach (var prereqId in node.prerequisiteNodeIds)
                {
                    if (prereqId == nodeId) continue; // self-ref already reported
                    if (stack.Contains(prereqId))
                    {
                        errors.Add($"检测到前置环：{nodeId} → {prereqId}。");
                        return true;
                    }
                    if (!visited.Contains(prereqId) && FindNode(prereqId) != null)
                    {
                        if (HasCycle(prereqId, visited, stack, errors))
                            return true;
                    }
                }
            }

            stack.Remove(nodeId);
            return false;
        }
    }
}
