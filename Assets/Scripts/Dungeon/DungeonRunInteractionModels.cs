using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 版本化升级 Offer 的候选种类。Dungeon 只识别稳定内容 ID 与提交种类；
    /// 卡牌、灵印的作者资产和展示均留在上层内容装配。
    /// </summary>
    public enum DungeonRunLevelRewardCandidateKind
    {
        Card,
        Seal
    }

    /// <summary>
    /// 向版本化升级 Offer 查询器传递的完整、可复现上下文。ReachedLevel 是
    /// 草稿中已算出的等级结果；它与合法候选一起提交，不是猜测的原始经验数值。
    /// </summary>
    public readonly struct DungeonRunLevelRewardOfferRequest
    {
        public string EffectId { get; }
        public int ReachedLevel { get; }
        public int RewardIndex { get; }
        public bool IsReferenceTargetLevel { get; }

        public DungeonRunLevelRewardOfferRequest(
            string effectId,
            int reachedLevel,
            int rewardIndex,
            bool isReferenceTargetLevel)
        {
            EffectId = effectId;
            ReachedLevel = Math.Max(1, reachedLevel);
            RewardIndex = Math.Max(0, rewardIndex);
            IsReferenceTargetLevel = isReferenceTargetLevel;
        }
    }

    /// <summary>固定升级 Offer 的一个稳定候选；显示名称绝不参与提交。</summary>
    public sealed class DungeonRunLevelRewardCandidate
    {
        public DungeonRunLevelRewardCandidateKind Kind { get; }
        public string ContentId { get; }

        public DungeonRunLevelRewardCandidate(
            DungeonRunLevelRewardCandidateKind kind,
            string contentId)
        {
            Kind = kind;
            ContentId = contentId;
        }
    }

    /// <summary>
    /// 一次版本化升级候选的作者结果。OfferContentId 必须能随着作者规则升级；
    /// 选择状态、存档恢复和合法性校验仍由 Dungeon 事务唯一持有。
    /// </summary>
    public sealed class DungeonRunLevelRewardOffer
    {
        public string OfferContentId { get; }
        public IReadOnlyList<DungeonRunLevelRewardCandidate> Candidates { get; }

        public DungeonRunLevelRewardOffer(
            string offerContentId,
            IReadOnlyList<DungeonRunLevelRewardCandidate> candidates)
        {
            OfferContentId = offerContentId;
            Candidates = candidates ?? Array.Empty<DungeonRunLevelRewardCandidate>();
        }
    }

    /// <summary>
    /// 内容／装配层按稳定等级结果提供固定候选的纯查询口。它不写 Run、UI 或
    /// 随机流，因而不会让 Dungeon 反向依赖 Content 的 ScriptableObject 类型。
    /// </summary>
    public interface IDungeonRunLevelRewardOfferResolver
    {
        bool TryResolve(
            DungeonRunLevelRewardOfferRequest request,
            out DungeonRunLevelRewardOffer offer,
            out string error);
    }

    public enum DungeonRunInteractionPhase
    {
        Ready,
        /// <summary>
        /// RewardChoice 同时承载升级奖励与固定地图卡牌候选；二者都只提交
        /// 稳定 contentId，显示文字不参与事务判定。
        /// </summary>
        RewardChoice,
        EvolutionChoice,
        SealChoice,
        SealTarget,
        Completed,
        Unavailable,
        /// <summary>Exact physical-card payments for the frozen two-lock Silver chest.</summary>
        SilverPayment
    }

    /// <summary>作者配方的运行边界：稳定主卡和结果卡，不保存模板对象身份。</summary>
    public sealed class DungeonRunEvolutionRecipe
    {
        public string ContentId { get; set; }
        public string PrimaryCardContentId { get; set; }
        /// <summary>旧存档／调用方兼容字段；正式进化与 RefineCard 均忽略。</summary>
        public IReadOnlyList<string> SecondaryCardContentIds { get; set; } = Array.Empty<string>();
        public string ResultCardContentId { get; set; }
    }

    /// <summary>同步地图事务的经济参与者；恢复余额不再产生获得事件或累计收益。</summary>
    public interface IDungeonRunInteractionWallet
    {
        int Balance { get; }
        JadeGainResult Preview(int baseAmount);
        JadeGainResult Credit(int baseAmount);
        void RestoreBalance(int balance);
    }

    /// <summary>
    /// Explicit cash-out capability for a source contract that excludes Greed.
    /// The caller owns the configured amount and durable transaction; this
    /// participant returns CashOut/false receipts and records actual Run income.
    /// It does not reuse dungeon-completion grants, which exclude Run income.
    /// </summary>
    public interface IDungeonRunFlatCashOutWallet : IDungeonRunInteractionWallet
    {
        JadeGainResult PreviewFlatCashOut(int baseAmount);
        JadeGainResult CreditFlatCashOut(int baseAmount);
    }

    /// <summary>装配层提供已解析内容；Run 状态及任何选择进度均不属于此目录。</summary>
    public sealed class DungeonRunInteractionCatalog
    {
        public IReadOnlyList<CardConfig> Cards { get; set; } = Array.Empty<CardConfig>();
        public IReadOnlyList<DungeonRunEvolutionRecipe> Recipes { get; set; } = Array.Empty<DungeonRunEvolutionRecipe>();
        public IReadOnlyList<CardConfig> RewardCards { get; set; } = Array.Empty<CardConfig>();
        public RewardConfig RewardConfig { get; set; } = new RewardConfig();
        /// <summary>
        /// 已经内容／账号门禁解析的自然卡印池。useNaturalCardSealPool 为 true 时
        /// 必须提供；不以 RewardCards 或固定排程替代缺失的自然池。
        /// </summary>
        public NaturalLevelUpRewardPool NaturalRewardPool { get; set; }
        public Func<string, bool> IsEligible { get; set; }
        /// <summary>Required output release gate for new evolution; independent of legacy grant admission.</summary>
        public Func<string, bool> IsEvolutionResultEligible { get; set; }
        public int GrowthPercent { get; set; }
        public int LuckPercent { get; set; }
        public IDungeonRunInteractionWallet Wallet { get; set; }
        /// <summary>Live account permission projected by the caller. Missing authority never grants seals.</summary>
        public bool CanReceiveSeals { get; set; }
        // Borrowed catalog data stays untouched; each transaction intersects its account
        // projection with the authoritative live Run permission on a detached view.
        internal DungeonRunInteractionCatalog RestrictSealPermission(bool runPermission)
        {
            var view = (DungeonRunInteractionCatalog)MemberwiseClone();
            view.CanReceiveSeals = CanReceiveSeals && runPermission;
            return view;
        }
        /// <summary>Explicit immutable Gem generation projection. Missing inputs remain unavailable.</summary>
        public IDungeonRunGemChestOfferResolver GemChestOfferResolver { get; set; }
        /// <summary>
        /// 装配层按账号解锁与宝箱品阶解析后的版本化灵印池。Dungeon 只在
        /// 首次正式开启时无放回抽样，并把稳定 contentId 冻结进 Run 存档。
        /// </summary>
        public DungeonRunSealRewardPool SealRewardPool { get; set; }
        /// <summary>
        /// 同一层可同时存在铜纹、银纹等多个版本化印匣池。旧调用方仍可只提供
        /// SealRewardPool；查询时会先匹配此集合，再回退到旧单池入口。
        /// </summary>
        public IReadOnlyList<DungeonRunSealRewardPool> SealRewardPools
            { get; set; } = Array.Empty<DungeonRunSealRewardPool>();
        /// <summary>
        /// 可选的版本化固定升级候选解析器，优先于自然／旧随机池。只有明确未命中
        /// 且无错误才使用配置对应的随机池；ReachLevel 请求携带 reference target
        /// level 标记，不需要让 Dungeon 依赖 Content 的作者 Profile。
        /// </summary>
        public IDungeonRunLevelRewardOfferResolver LevelRewardOfferResolver { get; set; }

        internal CardConfig FindCard(string id)
        {
            foreach (CardConfig card in Cards)
                if (card != null && string.Equals(card.contentId, id, StringComparison.OrdinalIgnoreCase))
                    return card;
            return null;
        }

        internal bool CanGrant(string id)
            => FindCard(id) != null && (IsEligible == null || IsEligible(id));

        /// <summary>查询产物是否同时满足内容、持久性和装配层的发布资格。</summary>
        public bool CanEvolveTo(string id)
            => CanGrant(id) && !FindCard(id).IsEncounterTemporary &&
               IsEvolutionResultEligible != null && IsEvolutionResultEligible(id);

        internal DungeonRunSealRewardPool FindSealRewardPool(string contentId)
        {
            foreach (DungeonRunSealRewardPool pool in
                     SealRewardPools ?? Array.Empty<DungeonRunSealRewardPool>())
            {
                if (pool != null && string.Equals(
                        pool.ContentId, contentId,
                        StringComparison.OrdinalIgnoreCase))
                    return pool;
            }
            return SealRewardPool != null && string.Equals(
                SealRewardPool.ContentId, contentId,
                StringComparison.OrdinalIgnoreCase)
                    ? SealRewardPool
                    : null;
        }
    }

    public sealed class DungeonRunSealRewardPool
    {
        public string ContentId { get; set; }
        public IReadOnlyList<CardSealSpec> Candidates { get; set; } =
            Array.Empty<CardSealSpec>();
        /// <summary>
        /// 抽取时优先保证的候选数。优先集合为空或不足时，由完整候选池补齐，
        /// 因而低解锁账号不会让印匣失效。
        /// </summary>
        public int PreferredCandidateCount { get; set; }
        public IReadOnlyList<string> PreferredCandidateContentIds
            { get; set; } = Array.Empty<string>();
        /// <summary>装配层的正式进化结果发布门禁；不授予账号解锁资格。</summary>
        public Func<string, bool> IsEvolutionResultEligible { get; set; }
    }

    /// <summary>选择键含稳定配方/内容 ID 和精确实例；显示文字从不参与提交匹配。</summary>
    public sealed class DungeonRunInteractionOption
    {
        public string Id { get; internal set; }
        public string CardContentId { get; internal set; }
        public int CardRuntimeInstanceId { get; internal set; }
        /// <summary>旧回执兼容字段；无副卡进化始终为 0。</summary>
        public int SecondaryRuntimeInstanceId { get; internal set; }
        public CardSealSpec Seal { get; internal set; }
        public string Title { get; internal set; }
        public string Description { get; internal set; }
    }

    public sealed class DungeonRunInteractionOffer
    {
        public DungeonRunInteractionPhase Phase { get; internal set; }
        public string Title { get; internal set; }
        public string Message { get; internal set; }
        public IReadOnlyList<DungeonRunInteractionOption> Options { get; internal set; } = Array.Empty<DungeonRunInteractionOption>();
        public IReadOnlyList<string> PaymentRequirements { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> PaymentRemainingRequirements { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<int> PaymentSelectedRuntimeInstanceIds { get; internal set; } = Array.Empty<int>();
        public CardSealSpec SelectedSeal { get; internal set; }
        public LevelInteractionReceipt Receipt { get; internal set; }
    }

    [Serializable]
    internal sealed class DungeonRunInteractionSaveData
    {
        public string nodeContentId;
        public string effectId;
        // 字段名必须兼容已写入的 GrantExperience 检查点；ReachLevel 复用它表示
        // “目标等级已前推，仍在等待该等级的奖励选择”。
        public int experienceOrigin; // 0 existing map XP; 1 one proven Crystal hit.
        public int crystalHitOrdinal;
        public int crystalCardRuntimeInstanceId;
        public int crystalAdjustedExperience;
        public bool experienceStarted;
        public int settledExperience;
        public int experienceBefore;
        public int rewardIndex;
        public int rewardReachedLevel;
        public LevelUpRewardSource rewardSource;
        public string rewardRuleContentId;
        public int rewardRuleVersion;
        // 固定升级 Offer 的稳定作者 ID。候选仍逐项保存，因而排程随后升级时，
        // 已打开的地图检查点不会重新查询或漂移。
        public string rewardOfferContentId;
        public List<string> rewardCardIds = new List<string>();
        // 0 保留旧 rewardCardIds 协议；1 使用类型化自然候选及真实灵印规格。
        public int naturalRewardVersion;
        public List<DungeonRunNaturalRewardCandidateSaveData> naturalRewardCandidates =
            new List<DungeonRunNaturalRewardCandidateSaveData>();
        public string rewardSealPoolContentId;
        public List<string> rewardSealIds = new List<string>();
        public int targetLevelBefore;
        public int targetLevelAfter;
        public string selectedSealContentId;
        public List<string> committedChoiceIds = new List<string>();
        public List<DungeonRunInteractionDecisionSaveData> completed = new List<DungeonRunInteractionDecisionSaveData>();
    }

    /// <summary>
    /// 自然升级候选的持久快照。卡牌以正式 contentId 解析；灵印规格完整克隆，
    /// 不从名称反推效果。展示文字只随快照保存，永远不参与合法性或选择键。
    /// </summary>
    [Serializable]
    internal sealed class DungeonRunNaturalRewardCandidateSaveData
    {
        public DungeonRunLevelRewardCandidateKind kind;
        public string contentId;
        public CardSealSpec seal;
        public string title;
        public string description;
    }

    [Serializable]
    internal sealed class DungeonRunInteractionDecisionSaveData
    {
        public string effectId;
        public string choiceId;
    }

    /// <summary>仅在同步提交期间存活的深拷贝；不会把牌组集合开放给其他模块。</summary>
    internal sealed class DungeonRunInteractionDraft
    {
        internal List<DungeonRunCardSaveData> Cards;
        internal CombatProgressionState Progression;
        internal DungeonRunExperienceCollections ExperienceCollections;
        internal int ForcedOpeningCardId;
    }
}
