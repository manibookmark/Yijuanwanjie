using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>Whether the physical card owns complete frozen author evidence. A legacy
    /// operational configuration is usable in combat but never proves its original basis.</summary>
    public enum CardAuthorProvenance { LegacyUnknown = 0, FrozenKnown = 1 }

    /// <summary>
    /// Run persistence boundary for a physical card instance.  Encounter-only
    /// cards may exist in any combat zone, but must be removed before the
    /// combat result is handed back to DungeonRunSession.
    /// </summary>
    public enum CardLifetime
    {
        Persistent = 0,
        EncounterTemporary = 1
    }

    /// <summary>Explicit construction choice; selecting it does not authorize a Run offering policy.</summary>
    public enum PersistentDuplicateLimitBreakPolicy
    {
        Reset = 0,
        Preserve = 1
    }

    /// <summary>
    /// 牌库中真实存在的一张卡。持有可变的实例状态，引用只读的 CardDefinition。
    /// 两张相同 definitionId 的卡拥有不同 instanceId。
    /// Cost-state review: the turn's numeric modifier and its Confuse provenance are one
    /// instance-owned value; draw/copy/UI owners never infer or separately maintain it.
    /// </summary>
    public sealed class CardInstance
    {
        /// <summary>本局/存档内唯一实例 ID。</summary>
        public int InstanceId { get; }

        /// <summary>对应 CardDefinition.contentId。</summary>
        public string DefinitionId { get; }

        /// <summary>运行时定义引用（不参与序列化）</summary>
        public CardDefinition Definition { get; private set; }

        // Core-only immutable provenance; sharing it across restored instances and
        // planning branches is safe because it cannot expose or mutate its snapshot.
        internal CardBaseTemplate BaseTemplate { get; private set; }
        internal CardBaseTemplate CurrentTemplate { get; private set; }
        public CardAuthorProvenance AuthorProvenance { get; private set; } = CardAuthorProvenance.LegacyUnknown;
        public bool HasKnownAuthorConfigurations => AuthorProvenance == CardAuthorProvenance.FrozenKnown;

        /// <summary>Copy's intrinsic lifetime/destination policy, not an attached seal.</summary>
        public bool IsSealCopy { get; private set; }

        // ========== 实例级可变状态 ==========

        /// <summary>当前回合该实例使用次数（回合开始清零，达到阈值碎裂）</summary>
        public int TurnUseCount { get; private set; }

        /// <summary>是否已销毁（碎裂或使用后销毁）</summary>
        public bool IsDestroyed { get; private set; }

        /// <summary>当前回合的数字费用偏移；新回合统一清除，不修改作者费用。</summary>
        public int TemporaryCostModifier { get; private set; }

        /// <summary>Confuse assigned this turn's cost, including a sampled zero offset.</summary>
        public bool IsCostConfusedThisTurn { get; private set; }

        /// <summary>
        /// 此卡实例已取得的突破层数。作者规则仍属于 CardDefinition/DamageSpec；
        /// 这里只拥有每张实体牌独立变化的进度，绝不写回共享定义。
        /// </summary>
        public int LimitBreakCount { get; private set; }

        /// <summary>Encounter-only uses since the last real draw, never a turn-retain flag.</summary>
        public int RemainingReturnUses { get; private set; }

        /// <summary>
        /// EncounterTemporary never crosses a Run capture boundary.  This
        /// state belongs to the instance rather than the shared author
        /// definition, so a content card can be granted persistently elsewhere.
        /// </summary>
        public CardLifetime Lifetime { get; private set; } =
            CardLifetime.Persistent;

        // Injection provenance is instance state, not a counter on an enemy.
        // DeckManager alone decides membership by scanning its current zones.
        // A new gameplay copy keeps the lifetime but not this provenance.
        internal string EnemyInjectedCardContentId { get; private set; }
        internal string EnemySourceBehaviorContentId { get; private set; }

        // ========== 构造 ==========

        public CardInstance(int instanceId, CardDefinition definition)
            : this(instanceId, definition?.contentId, definition)
        {
        }

        /// <summary>
        /// CardConfig 迁移门面使用的构造入口。正式 CardDefinition 尚未接管牌区前，
        /// 仍由同一个 CardInstance 拥有实例 ID 与全部新增实例状态。
        /// </summary>
        internal CardInstance(int instanceId, string definitionId)
            : this(instanceId, definitionId, null)
        {
        }

        private CardInstance(
            int instanceId,
            string definitionId,
            CardDefinition definition)
        {
            if (instanceId <= 0)
                throw new System.ArgumentOutOfRangeException(
                    nameof(instanceId),
                    "卡牌实例 ID 必须大于 0。");

            InstanceId = instanceId;
            Definition = definition;
            DefinitionId = definitionId;
        }

        // ========== 委托属性 ==========

        public string CardName => Definition?.cardName;
        public CardColor Color => Definition?.color ?? CardColor.Huo;
        public CostType CostType => Definition?.costType ?? CostType.Default;
        public string VolumeId => Definition?.volumeId;
        public bool DestroyOnUse => IsSealCopy || (Definition?.destroyOnUse ?? false);

        /// <summary>获取实际费用（含临时修改）</summary>
        public int GetCost(int statValue = 0)
        {
            int baseCost = Definition?.GetCost(statValue) ?? 0;
            return CostType == CostType.Wild || CostType == CostType.X
                ? baseCost : ApplyTemporaryCostTo(baseCost);
        }

        /// <summary>
        /// Applies this instance's numeric modifier without discarding negative
        /// costs. Wild/X callers retain their separate cost identity. Saturation
        /// protects integer bounds; zero is not a minimum payment cost.
        /// </summary>
        internal int ApplyTemporaryCostTo(int baseCost) =>
            (int)Math.Max(int.MinValue, Math.Min(int.MaxValue,
                (long)baseCost + TemporaryCostModifier));

        public bool DoesCardCombo() => Definition?.DoesCardCombo() ?? true;

        // ========== 状态操作 ==========

        /// <summary>回合开始时重置临时状态</summary>
        public void ResetTurnState()
        {
            TurnUseCount = 0;
            TemporaryCostModifier = 0;
            IsCostConfusedThisTurn = false;
        }

        /// <summary>记录一次使用，返回使用后的总次数</summary>
        public int RecordUse()
        {
            if (TurnUseCount < int.MaxValue) TurnUseCount++;
            return TurnUseCount;
        }

        internal void ResetReturnUsesOnDraw(IReadOnlyList<CardSealSpec> seals)
            => RemainingReturnUses = CardSealRules.GetReturnUsesPerDraw(seals);

        internal bool TryConsumeReturnUse()
        {
            if (RemainingReturnUses <= 0 || IsDestroyed) return false;
            RemainingReturnUses--;
            return true;
        }

        /// <summary>标记销毁</summary>
        public void MarkDestroyed()
        {
            IsDestroyed = true;
        }

        /// <summary>Assigns a temporary cost from a source other than Confuse.</summary>
        public void SetTemporaryCostModifier(int amount)
        {
            TemporaryCostModifier = amount;
            IsCostConfusedThisTurn = false;
        }

        /// <summary>Assigns the resolved offset and its source together, without sampling.</summary>
        internal void SetConfusedCostModifier(int amount)
        {
            TemporaryCostModifier = amount;
            IsCostConfusedThisTurn = true;
        }

        /// <summary>
        /// 为这一张实体牌增加突破层数。返回增加后的总层数；溢出会明确失败，
        /// 不允许整数回绕把高突破变成负数。
        /// </summary>
        public int ApplyLimitBreak(int levels = 1)
        {
            if (levels <= 0)
                throw new System.ArgumentOutOfRangeException(
                    nameof(levels),
                    "突破层数增量必须大于 0。");
            LimitBreakCount = checked(LimitBreakCount + levels);
            return LimitBreakCount;
        }

        /// <summary>
        /// 仅供版本化存档/迁移门面恢复精确进度；负值属于损坏数据。
        /// </summary>
        internal void RestoreLimitBreakCount(int count)
        {
            if (count < 0)
                throw new System.ArgumentOutOfRangeException(
                    nameof(count),
                    "突破层数不能小于 0。");
            LimitBreakCount = count;
        }

        internal void MarkEncounterTemporary()
        {
            Lifetime = CardLifetime.EncounterTemporary;
        }

        /// <summary>The factory binds the immutable evidence and its classification together.
        /// Legacy cards have no authoritative base; their current template is operational only.</summary>
        internal void BindAuthorConfigurations(CardBaseTemplate basis, CardBaseTemplate current,
            CardAuthorProvenance provenance)
        {
            if (current == null || current.ContentId != DefinitionId)
                throw new ArgumentException("Current template must match this physical definition.");
            if (provenance != CardAuthorProvenance.LegacyUnknown && provenance != CardAuthorProvenance.FrozenKnown)
                throw new ArgumentOutOfRangeException(nameof(provenance));
            if (provenance == CardAuthorProvenance.FrozenKnown ? basis == null : basis != null)
                throw new ArgumentException("Author evidence must agree with its provenance classification.");
            BaseTemplate = basis; CurrentTemplate = current; AuthorProvenance = provenance;
        }

        // Retain the prior Core binding seam. It cannot upgrade legacy evidence;
        // factory capture/construction still validates the physical identity.
        internal void BindCurrentTemplate(CardBaseTemplate template)
        {
            CurrentTemplate = template ?? throw new ArgumentNullException(nameof(template));
        }

        internal void MarkSealCopy()
        {
            MarkEncounterTemporary();
            IsSealCopy = true;
        }

        internal void MarkEnemyInjection(string cardContentId, string behaviorContentId)
        {
            MarkEncounterTemporary();
            EnemyInjectedCardContentId = cardContentId;
            EnemySourceBehaviorContentId = behaviorContentId;
        }

        /// <summary>
        /// Restoration/replacement of the same physical card preserves its
        /// encounter lifetime, injection group and current-turn cost. A new copied card
        /// must not call this method: it inherits neither injection nor turn effects.
        /// </summary>
        internal void RestoreEncounterStateFrom(CardInstance source)
        {
            if (source == null) return;
            if (source.InstanceId != InstanceId)
                throw new ArgumentException("恢复遭遇状态必须保留同一实例 ID。", nameof(source));
            RemainingReturnUses = source.RemainingReturnUses;
            TurnUseCount = source.TurnUseCount;
            TemporaryCostModifier = source.TemporaryCostModifier;
            IsCostConfusedThisTurn = source.IsCostConfusedThisTurn;
            Lifetime = source.Lifetime;
            IsSealCopy = source.IsSealCopy;
            EnemyInjectedCardContentId = source.EnemyInjectedCardContentId;
            EnemySourceBehaviorContentId = source.EnemySourceBehaviorContentId;
        }
    }

}
