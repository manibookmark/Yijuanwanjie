using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>牌组输入的显式语义；禁止再从 runtimeInstanceId 零值猜测。</summary>
    public enum CardDeckInitialization
    {
        CreateNewInstances = 0,
        RestoreInstances = 1
    }

    /// <summary>Only pre-primary validation/allocation failures use this wrapper.
    /// Observer exceptions escape unchanged, because the primary may already be observable.</summary>
    internal sealed class EnemyTemporaryCardPreparationException : Exception
    {
        internal EnemyTemporaryCardPreparationException(Exception inner)
            : base("Enemy temporary-card preparation failed: " + inner.Message, inner) { }
    }

    /// <summary>
    /// 新牌授予事务的不可变回执；新牌直接进入手牌。
    /// </summary>
    public readonly struct CardGrantResult
    {
        public CardConfig Card { get; }
        public bool AddedToHand { get; }

        internal CardGrantResult(CardConfig card, bool addedToHand)
        {
            Card = card;
            AddedToHand = addedToHand;
        }
    }

    /// <summary>
    /// Receipt of a completed enemy-injection transaction, not the advertised
    /// intent count. Cards contains only the actual new instances. Reaching
    /// the shared residual limit is a successful zero/partial injection.
    /// </summary>
    public readonly struct EnemyTemporaryCardGrantResult
    {
        private readonly IReadOnlyList<CardConfig> _cards;

        internal EnemyTemporaryCardGrantResult(
            EnemyTemporaryCardRequest request,
            List<CardConfig> cards, EncounterTemporaryCardPlacement placement,
            bool canceledByEncounterEnd = false)
        {
            Request = request;
            Placement = placement;
            _cards = cards.AsReadOnly();
            CanceledByEncounterEnd = canceledByEncounterEnd;
        }

        public EnemyTemporaryCardRequest Request { get; }
        /// <summary>Resolved destination of this transaction, independent of the authored conditional mode.</summary>
        public EncounterTemporaryCardPlacement Placement { get; }
        public bool AddedToHand => ActualCount > 0 && Placement == EncounterTemporaryCardPlacement.Hand;
        public IReadOnlyList<CardConfig> Cards => _cards ?? Array.Empty<CardConfig>();
        public int RequestedCount => Request.Count;
        public int ActualCount => Cards.Count;
        public bool CanceledByEncounterEnd { get; }
        public bool LimitReached => !CanceledByEncounterEnd && ActualCount < RequestedCount;
    }

    /// <summary>
    /// 牌库/手牌/弃牌堆管理
    /// card-game skill: zones + draw with automatic reshuffle, exactly one zone per card
    /// 规模审查：钤印替换只扩展既有牌区事务；赋予规则归 CardSealRules，
    /// 本类保持实体所在牌区与位置，不新增奖励选择或内容发布生命周期。
    /// G3规模复核：本类即一个遭遇的牌区owner，残留上限直接查询三个牌区的
    /// 注入实例，不另建敌人计数器或死亡订阅。来源仅为实例元数据；本次仍是
    /// 牌区授予/清理事务。出现独立牌区生命周期或战斗中持久化协议时重新审查。
    /// G3F1复核：返手的Resolving暂存仍是本类唯一牌区事务；次数归CardInstance，
    /// 仅真正抽牌重置。暂存同时参与实例ID去重、污染上限和临时牌清理。
    /// Copy复核：基础模板归CardBaseTemplate，不在这里重建作者字段。此处只拥有
    /// 新牌入手和遭遇内已使用ID集合，避免销毁/终局移除后复用旧实体身份。
    /// Ordered grant规模复核：同步准备批次仍归本牌区owner，隐藏创建、插位和取消。
    /// 不新增牌区或跨帧lease；只保护结构写入，标量规则保持原入口。
    /// 若增加跨帧准备/持久化或另一牌区owner，重新审查并封装该完整生命周期。
    /// Retain复核：未使用资格归CardInstance.TurnUseCount；本类在真实离手记录，
    /// 正常回合末先判定、下回合开始才重置。没有第二套“已使用/保留”集合。
    /// 请灵复核：这里只把调用方给出的显式起手集合全部归入同一次抽牌，不持有邀请或下场保证状态。
    /// 扰灵回执复核：本类仍唯一写入实体临时费用；在原写入处记录局部不可变差值，
    /// 不新增随机、规则或跨帧状态。若引入另一费用写入者，再重新审查此边界。
    /// 扰灵来源复核：赋费同时提交实例来源，0偏移仍受扰；标记与回合清理均归CardInstance，
    /// 本类不保存状态副本。两处既有随机赋值的目标、次数与范围保持不变。
    /// </summary>
    public class DeckManager
    {
        private readonly List<CardConfig> _deck = new List<CardConfig>();
        private readonly List<CardConfig> _hand = new List<CardConfig>();
        private readonly List<CardConfig> _discard = new List<CardConfig>();
        // A returning card is neither in hand nor drawable while its effects
        // resolve. This transient zone is owned here, never by presentation.
        private readonly List<CardConfig> _resolvingReturns = new List<CardConfig>();
        private readonly ReadOnlyCollection<CardConfig> _deckView;
        private readonly ReadOnlyCollection<CardConfig> _handView;
        private readonly ReadOnlyCollection<CardConfig> _discardView;
        private int _standardDrawCount = 5;
        private readonly int _baseStandardDrawCount;
        private int _explicitDrawBonus;
        private readonly int _baseExplicitDrawBonus;
        // This synchronous reservation belongs to the existing zone owner, not
        // another pile. Its cards are invisible to draws, capacity and observers.
        private sealed class PreparedEnemyTemporaryGrant
        {
            internal readonly EnemyTemporaryCardRequest Request;
            internal readonly EncounterTemporaryCardPlacement Placement;
            internal readonly List<CardConfig> Cards;
            internal readonly List<int> InsertionIndices;
            internal bool Canceled;
            internal bool Committed;

            internal PreparedEnemyTemporaryGrant(EnemyTemporaryCardRequest request,
                EncounterTemporaryCardPlacement placement, int count)
            {
                Request = request;
                Placement = placement;
                Cards = new List<CardConfig>(count);
                InsertionIndices = new List<int>(count);
            }
        }

        private PreparedEnemyTemporaryGrant _preparedEnemyTemporaryGrant;

        private void RequireAvailableCardZones()
        {
            if (_preparedEnemyTemporaryGrant != null)
                throw new InvalidOperationException(
                    "Card zones are reserved by an ordered enemy temporary-card grant.");
        }

        private static void DestroyUnplacedEnemyCards(PreparedEnemyTemporaryGrant prepared)
        {
            if (prepared.Committed) return;
            foreach (CardConfig card in prepared.Cards)
                if (card != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(card);
                    else UnityEngine.Object.DestroyImmediate(card);
                }
            prepared.Cards.Clear();
        }

        private static void ApplyNoEnemyPrimary() { }

        private IRandomSource _random;
        private readonly Predicate<CardConfig> _canDraw;
        private readonly IInstanceIdGenerator _instanceIds;
        private readonly HashSet<int> _issuedInstanceIds = new HashSet<int>();

        public IReadOnlyList<CardConfig> Deck => _deckView;
        public IReadOnlyList<CardConfig> Hand => _handView;
        public IReadOnlyList<CardConfig> Discard => _discardView;
        public int DeckCount => _deck.Count;
        public int HandCount => _hand.Count;
        public int DiscardCount => _discard.Count;
        /// <summary>历史 API 名称；值为每回合标准抽牌数，不是手牌持有上限。</summary>
        public int HandLimit => _standardDrawCount;
        /// <summary>当前引卷值：只修正规则明确发起的显式抽牌。</summary>
        public int ExplicitDrawBonus => _explicitDrawBonus;

        public DeckManager(
            IReadOnlyList<CardConfig> initialDeck,
            int handLimit = 5,
            Predicate<CardConfig> canDraw = null,
            IInstanceIdGenerator instanceIds = null,
            int explicitDrawBonus = 0)
            : this(
                initialDeck,
                handLimit,
                canDraw,
                instanceIds,
                explicitDrawBonus,
                CardDeckInitialization.CreateNewInstances)
        {
        }

        /// <summary>
        /// 从已经过存档边界恢复的实体牌建立牌区。与公共构造器的新牌语义分离，
        /// 不接受 ID=0 的作者定义，也不会根据哨兵值自动切换模式。
        /// </summary>
        public static DeckManager Restore(
            IReadOnlyList<CardConfig> restoredDeck,
            int handLimit = 5,
            Predicate<CardConfig> canDraw = null,
            IInstanceIdGenerator instanceIds = null,
            int explicitDrawBonus = 0)
            => new DeckManager(
                restoredDeck,
                handLimit,
                canDraw,
                instanceIds,
                explicitDrawBonus,
                CardDeckInitialization.RestoreInstances);

        private DeckManager(
            IReadOnlyList<CardConfig> initialDeck,
            int handLimit,
            Predicate<CardConfig> canDraw,
            IInstanceIdGenerator instanceIds,
            int explicitDrawBonus,
            CardDeckInitialization initialization)
        {
            if (initialDeck == null)
                throw new ArgumentNullException(nameof(initialDeck));
            _instanceIds = instanceIds ?? DefaultInstanceIdGenerator.Instance;
            _deckView = _deck.AsReadOnly();
            _handView = _hand.AsReadOnly();
            _discardView = _discard.AsReadOnly();
            bool restoreInstances = initialization ==
                CardDeckInitialization.RestoreInstances;
            // 恢复入口先冻结并校验全部 ID；新建入口完全忽略来源对象可能携带
            // 的实例状态，两种语义不会因输入顺序或零值哨兵而互相切换。
            var seenInstanceIds = new HashSet<int>();
            for (int i = 0; i < initialDeck.Count; i++)
            {
                CardConfig source = initialDeck[i];
                if (source == null)
                    throw new ArgumentException(
                        $"初始牌组第 {i} 张牌为空。",
                        nameof(initialDeck));
                if (!restoreInstances) continue;

                int restoredId = source.runtimeInstanceId;
                if (restoredId <= 0 || !seenInstanceIds.Add(restoredId))
                {
                    throw new ArgumentException(
                        $"恢复牌组包含无效或重复运行实例 ID：{restoredId}。",
                        nameof(initialDeck));
                }
            }

            for (int i = 0; i < initialDeck.Count; i++)
            {
                CardConfig source = initialDeck[i];
                CardConfig instance;
                if (restoreInstances)
                {
                    instance = CardInstanceFactory.CloneRestored(source);
                }
                else
                {
                    int generatedId;
                    do
                    {
                        generatedId = _instanceIds.NextId();
                    }
                    while (generatedId <= 0 ||
                           !seenInstanceIds.Add(generatedId));
                    instance = CardInstanceFactory.CreateNew(
                        source,
                        generatedId);
                }
                _deck.Add(instance);
            }
            _issuedInstanceIds.UnionWith(seenInstanceIds);
            _standardDrawCount = Math.Max(0, handLimit);
            _baseStandardDrawCount = _standardDrawCount;
            _explicitDrawBonus = Math.Max(0, explicitDrawBonus);
            _baseExplicitDrawBonus = _explicitDrawBonus;
            _canDraw = canDraw ?? (_ => true);
        }

        /// <summary>
        /// 提高本遭遇的回合抽牌数，不立即抽牌；返回实际增量。
        /// </summary>
        public int IncreaseHandLimit(int amount)
        {
            if (amount <= 0) return 0;
            int before = _standardDrawCount;
            _standardDrawCount = amount > int.MaxValue - _standardDrawCount
                ? int.MaxValue
                : _standardDrawCount + amount;
            return _standardDrawCount - before;
        }

        /// <summary>提高本遭遇的引卷；返回实际增量。</summary>
        public int IncreaseExplicitDrawBonus(int amount)
        {
            if (amount <= 0) return 0;
            int before = _explicitDrawBonus;
            _explicitDrawBonus = amount > int.MaxValue - _explicitDrawBonus
                ? int.MaxValue
                : _explicitDrawBonus + amount;
            return _explicitDrawBonus - before;
        }

        /// <summary>
        /// 执行一次回合抽牌事务：强制牌计入本次展卷数量，已有手牌不占抽牌额度。
        /// 返回实际进入手牌的牌，调用方无需读取或修改牌区内部集合。
        /// </summary>
        public IReadOnlyList<CardConfig> RefillStandard(
            IReadOnlyList<CardConfig> forcedOpeningCards = null)
        {
            RequireAvailableCardZones();
            var drawn = new List<CardConfig>();
            if (forcedOpeningCards != null)
            {
                for (int i = 0;
                     i < forcedOpeningCards.Count;
                     i++)
                {
                    CardConfig forced = forcedOpeningCards[i];
                    if (forced == null || drawn.Exists(card => ReferenceEquals(card, forced) ||
                        forced.runtimeInstanceId > 0 && card.runtimeInstanceId == forced.runtimeInstanceId)) continue;

                    CardConfig moved;
                    bool found = forced.runtimeInstanceId > 0
                        ? TryDrawInstanceToHand(
                            forced.runtimeInstanceId, out moved)
                        : TryDrawToHand(forced.contentId, out moved);
                    if (found) drawn.Add(moved);
                }
            }

            // Explicit simultaneous opening guarantees may exceed the normal draw budget.
            // Preserve every guaranteed physical card; this changes only this one opening draw.
            DrawInto(Math.Max(0, _standardDrawCount - drawn.Count), drawn);
            return drawn;
        }

        /// <summary>
        /// 执行一次显式抽牌。引卷只在基础请求大于零时追加，
        /// 仅受可抽过滤与牌堆实际数量约束，手牌持有数量无限制。
        /// </summary>
        public IReadOnlyList<CardConfig> DrawExplicit(int baseCount)
        {
            if (baseCount <= 0) return Array.Empty<CardConfig>();
            RequireAvailableCardZones();
            long requested = (long)baseCount + _explicitDrawBonus;
            var drawn = new List<CardConfig>();
            DrawInto((int)Math.Min(int.MaxValue, requested), drawn);
            return drawn;
        }

        /// <summary>
        /// 遭遇结束时恢复入场快照。牌区本身即将销毁，不在这里移动现有卡牌。
        /// </summary>
        public void ResetEncounterModifiers()
        {
            _standardDrawCount = _baseStandardDrawCount;
            _explicitDrawBonus = _baseExplicitDrawBonus;
        }

        /// <summary>
        /// Begin a new player turn before standard draws. Never call during draw,
        /// return completion or end-turn cleanup: Retain must still see that turn's
        /// committed uses. Return's per-draw budget is intentionally not reset here.
        /// </summary>
        internal void ResetTurnState()
        {
            foreach (List<CardConfig> zone in new[] { _deck, _hand, _discard, _resolvingReturns })
                foreach (CardConfig card in zone)
                    card.RuntimeInstance.ResetTurnState();
        }

        /// <summary>
        /// Randomizes the current-turn cost of numeric cards in hand.  The
        /// definition's manaCost remains immutable; only each physical
        /// CardInstance receives a temporary modifier, so turn reset and save
        /// restore cannot leak the effect into the Run deck.
        /// </summary>
        public int RandomizeNumericHandCosts(
            int minInclusive,
            int maxInclusive)
            => RandomizeNumericHandCosts(minInclusive, maxInclusive, out _);

        /// <summary>Preserves the original eligible-card count and RNG order;
        /// the detached receipt contains only actual numeric changes.</summary>
        public int RandomizeNumericHandCosts(
            int minInclusive,
            int maxInclusive,
            out IReadOnlyList<HandCardCostChange> costChanges)
        {
            if (minInclusive < 0)
                throw new ArgumentOutOfRangeException(nameof(minInclusive));
            if (maxInclusive < minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxInclusive));
            if (maxInclusive == int.MaxValue)
                throw new ArgumentOutOfRangeException(
                    nameof(maxInclusive),
                    "随机费用上限必须允许构造独占上界。");

            costChanges = Array.Empty<HandCardCostChange>();
            List<HandCardCostChange> changes = null;
            int changed = 0;
            for (int i = 0; i < _hand.Count; i++)
            {
                CardConfig card = _hand[i];
                if (card == null ||
                    card.RuntimeInstance == null ||
                    card.RuntimeInstance.IsDestroyed ||
                    card.costType != CostType.Default)
                    continue;

                int beforeCost = card.GetCost();
                int target = RandomRange(
                    minInclusive,
                    maxInclusive + 1);
                card.SetConfusedCostModifier(target - card.manaCost);
                int afterCost = card.GetCost();
                if (beforeCost != afterCost)
                {
                    changes ??= new List<HandCardCostChange>();
                    changes.Add(new HandCardCostChange(
                        card.runtimeInstanceId, beforeCost, afterCost));
                }
                changed++;
            }
            if (changes != null) costChanges = changes.AsReadOnly();
            return changed;
        }

        /// <summary>
        /// 按当前数字牌原费用施加一个临时随机偏移。对应 VC ConfuseEffect
        /// 的 min/max mana difference；普通数字费用允许为负。覆盖原偏移，
        /// 不累计上次扰灵，不修改共享定义。
        /// </summary>
        public int RandomizeNumericHandCostOffsets(
            int minOffsetInclusive,
            int maxOffsetInclusive)
            => RandomizeNumericHandCostOffsets(minOffsetInclusive, maxOffsetInclusive, out _);

        /// <summary>Captures each effective cost before and after the existing
        /// offset assignment, without accumulating or resampling that offset.</summary>
        public int RandomizeNumericHandCostOffsets(
            int minOffsetInclusive,
            int maxOffsetInclusive,
            out IReadOnlyList<HandCardCostChange> costChanges)
        {
            if (maxOffsetInclusive < minOffsetInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxOffsetInclusive));
            if (maxOffsetInclusive == int.MaxValue)
                throw new ArgumentOutOfRangeException(
                    nameof(maxOffsetInclusive),
                    "随机费用偏移上限必须允许构造独占上界。");

            costChanges = Array.Empty<HandCardCostChange>();
            List<HandCardCostChange> changes = null;
            int changed = 0;
            for (int i = 0; i < _hand.Count; i++)
            {
                CardConfig card = _hand[i];
                if (card == null || card.RuntimeInstance == null ||
                    card.RuntimeInstance.IsDestroyed || card.costType != CostType.Default)
                    continue;

                int beforeCost = card.GetCost();
                int offset = RandomRange(minOffsetInclusive, maxOffsetInclusive + 1);
                card.SetConfusedCostModifier(offset);
                int afterCost = card.GetCost();
                if (beforeCost != afterCost)
                {
                    changes ??= new List<HandCardCostChange>();
                    changes.Add(new HandCardCostChange(
                        card.runtimeInstanceId, beforeCost, afterCost));
                }
                changed++;
            }
            if (changes != null) costChanges = changes.AsReadOnly();
            return changed;
        }

        /// <summary>设置随机源。未设置时回退到 UnityEngine.Random。</summary>
        public void SetRandomSource(IRandomSource random)
        {
            _random = random;
        }

        private int RandomRange(int min, int max)
        {
            return _random != null ? _random.Range(min, max) : UnityEngine.Random.Range(min, max);
        }

        /// <summary>抽一张牌（空牌库自动洗入弃牌堆）</summary>
        private CardConfig DrawCard()
        {
            int drawableCount = CountDrawableCards(_deck);
            if (drawableCount == 0)
            {
                if (CountDrawableCards(_discard) == 0) return null;
                _deck.AddRange(_discard);
                _discard.Clear();
                Shuffle(_deck);
                drawableCount = CountDrawableCards(_deck);
            }

            int drawableOrdinal = RandomRange(0, drawableCount);
            int idx = FindDrawableIndex(_deck, drawableOrdinal);
            var card = _deck[idx];
            _deck.RemoveAt(idx);
            _hand.Add(card);
            card.RuntimeInstance.ResetReturnUsesOnDraw(card.seals);
            return card;
        }

        /// <summary>抽多张牌</summary>
        private void DrawInto(int count, List<CardConfig> receipt)
        {
            for (int i = 0;
                 i < Math.Max(0, count) &&
                 _deck.Count + _discard.Count > 0;
                 i++)
            {
                CardConfig card = DrawCard();
                if (card == null)
                    break;
                receipt?.Add(card);
            }
        }

        /// <summary>
        /// 定向抽牌：把牌库中第一张 contentId 匹配的牌移入手牌
        /// （教学强制起手，不消耗随机数）。牌库无合法匹配时返回 false。
        /// </summary>
        private bool TryDrawToHand(string contentId, out CardConfig drawn)
        {
            drawn = null;
            if (string.IsNullOrEmpty(contentId))
                return false;
            for (int i = 0; i < _deck.Count; i++)
            {
                if (!string.Equals(_deck[i].contentId, contentId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!_canDraw(_deck[i]))
                    continue;
                drawn = _deck[i];
                _deck.RemoveAt(i);
                _hand.Add(drawn);
                drawn.RuntimeInstance.ResetReturnUsesOnDraw(drawn.seals);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 按精确实例 ID 定向抽牌；不存在时不会回退到同 contentId 的副本，
        /// 也不消耗随机数。
        /// </summary>
        private bool TryDrawInstanceToHand(
            int runtimeInstanceId,
            out CardConfig drawn)
        {
            drawn = null;
            if (runtimeInstanceId <= 0)
                return false;
            for (int i = 0; i < _deck.Count; i++)
            {
                CardConfig candidate = _deck[i];
                if (candidate.runtimeInstanceId != runtimeInstanceId ||
                    !_canDraw(candidate))
                    continue;
                drawn = candidate;
                _deck.RemoveAt(i);
                _hand.Add(drawn);
                drawn.RuntimeInstance.ResetReturnUsesOnDraw(drawn.seals);
                return true;
            }
            return false;
        }

        /// <summary>Atomic zone-only play for callers with no intervening effects.</summary>
        public bool TryPlayFromHand(CardConfig card, bool destroyOnUse)
        {
            if (!BeginPlayFromHand(card, destroyOnUse, out _)) return false;
            CompletePlayFromHand(card);
            return true;
        }

        public bool WillReturnOnPlay(CardConfig card)
            => card != null && _hand.Contains(card) && !card.MustDestroyOnUse &&
               !card.RuntimeInstance.IsDestroyed &&
               card.RuntimeInstance.RemainingReturnUses > 0;

        /// <summary>
        /// Start a real play after payment succeeds. Destruction wins. A return
        /// consumes one draw-owned use and waits outside drawable zones until
        /// CompletePlayFromHand; neither transition is a draw or a new grant.
        /// </summary>
        public bool BeginPlayFromHand(CardConfig card, bool destroyOnUse,
            out bool returnsAfterResolution)
        {
            returnsAfterResolution = false;
            RequireAvailableCardZones();
            if (card == null || !_hand.Remove(card)) return false;
            card.RuntimeInstance.RecordUse();
            destroyOnUse |= card.MustDestroyOnUse;
            if (!destroyOnUse && card.RuntimeInstance.TryConsumeReturnUse())
            {
                returnsAfterResolution = true;
                _resolvingReturns.Add(card);
            }
            else if (!destroyOnUse)
                _discard.Add(card);
            else
                card.RuntimeInstance.MarkDestroyed();
            return true;
        }

        /// <summary>Idempotent completion; restoring the same object never refreshes its budget.</summary>
        public bool CompletePlayFromHand(CardConfig card)
        {
            RequireAvailableCardZones();
            if (card == null || !_resolvingReturns.Remove(card)) return false;
            if (!card.RuntimeInstance.IsDestroyed) _hand.Add(card);
            return true;
        }

        /// <summary>
        /// Offer sampling sees every persistent card still owned by this deck,
        /// including a return currently resolving outside drawable zones. This
        /// read does not make that transient zone selectable or assignable;
        /// target submission must wait until the play has completed.
        /// </summary>
        internal bool HasPersistentSealHost(CardSealSpec seal, CombatRuleSet rules,
            Func<string, bool> isEvolutionResultEligible)
        {
            foreach (List<CardConfig> zone in new[] { _deck, _hand, _discard, _resolvingReturns })
                foreach (CardConfig card in zone)
                    if (card != null && card.runtimeInstanceId > 0 &&
                        !card.IsEncounterTemporary && !card.RuntimeInstance.IsDestroyed &&
                        CardSealRules.CanAssign(card, seal, rules, isEvolutionResultEligible))
                        return true;
            return false;
        }

        /// <summary>
        /// 在宿主现有牌区原子赋予灵印。进化替换保持所在位置、实体 ID 与当回合免耗，
        /// 普通灵印保持原实体引用；失败不改牌区。调用方不接触牌区内部集合。
        /// </summary>
        public bool TryAssignSeal(
            int runtimeInstanceId,
            CardSealSpec seal,
            out CardConfig assigned,
            CombatRuleSet rules = null,
            Func<string, bool> isEvolutionResultEligible = null)
        {
            assigned = null;
            if (seal != null && seal.effect == CardSealEffect.Evolution)
                RequireAvailableCardZones();
            if (runtimeInstanceId <= 0)
                return false;
            foreach (List<CardConfig> zone in new[] { _deck, _hand, _discard })
            {
                int index = zone.FindIndex(card =>
                    card != null && card.runtimeInstanceId == runtimeInstanceId);
                if (index < 0) continue;
                CardConfig host = zone[index];
                if (!CardSealRules.CanAssign(host, seal, rules,
                        isEvolutionResultEligible))
                    return false;
                if (seal.effect == CardSealEffect.Evolution)
                {
                    assigned = CardSealRules.BuildAssignedCard(host, seal, rules,
                        isEvolutionResultEligible);
                    if (assigned == null) return false;
                    assigned.RuntimeInstance.RestoreEncounterStateFrom(host.RuntimeInstance);
                    assigned.isFreeToPlay = host.isFreeToPlay;
                    zone[index] = assigned;
                }
                else
                {
                    host.seals ??= new List<CardSealSpec>();
                    host.seals.Add(seal.Clone());
                    assigned = host;
                }
                return true;
            }
            return false;
        }

        /// <summary>回合结束：弃掉所有手牌</summary>
        public void DiscardHand()
        {
            RequireAvailableCardZones();
            _discard.AddRange(_hand);
            _hand.Clear();
        }

        /// <summary>
        /// Normal turn cleanup only. Retain leaves the same object in hand and never
        /// draws/rearms Return or consumes next turn's standard quota. Forced discard,
        /// play destinations and encounter teardown retain their existing semantics.
        /// </summary>
        public void DiscardHandAtTurnEnd()
        {
            RequireAvailableCardZones();
            for (int i = 0; i < _hand.Count;)
            {
                CardConfig card = _hand[i];
                if (!card.RuntimeInstance.IsDestroyed && card.RuntimeInstance.TurnUseCount == 0 &&
                    CardSealRules.HasEffect(card.seals, CardSealEffect.Retain))
                {
                    i++;
                    continue;
                }
                if (!card.RuntimeInstance.IsDestroyed) _discard.Add(card);
                _hand.RemoveAt(i);
            }
        }

        /// <summary>
        /// 把一张新卡授予到抽牌堆。返回新建的独立实例；定义源绝不被写入运行状态。
        /// </summary>
        public CardConfig GrantToDrawPile(CardConfig card)
        {
            if (card == null) return null;
            CardConfig granted = CreateNewInstance(card);
            _deck.Add(granted);
            return granted;
        }

        /// <summary>
        /// Compatibility entry point for an unlimited, ungrouped temporary
        /// card grant. The historical name is retained; placement is honored,
        /// including Hand. Authored enemy intents should instead pass the full
        /// request to GrantEnemyTemporaryCards so their shared cap is enforced.
        /// </summary>
        public CardConfig GrantEncounterTemporaryToDrawPile(
            CardConfig card,
            EncounterTemporaryCardPlacement placement =
                EncounterTemporaryCardPlacement.RandomNonTopDrawPile)
        {
            if (card == null) return null;
            if (!Enum.IsDefined(
                    typeof(EncounterTemporaryCardPlacement), placement) ||
                placement == EncounterTemporaryCardPlacement.HandDuringPlayerTurnElseRandomDrawPile)
            {
                throw new ArgumentOutOfRangeException(nameof(placement));
            }

            return GrantEncounterTemporaryCard(card, placement);
        }

        /// <summary>
        /// Executes a bound intent as a real card-zone transaction. This deck
        /// is the encounter scope; (injected card ID, source behavior ID) is
        /// shared by all enemies and actions in that encounter. Counted cards
        /// may be in draw, hand or discard. Destruction removes a slot through
        /// normal zone removal; discarding and source death do not free one.
        /// No player hand capacity, fallback damage or deferred grant exists.
        /// </summary>
        public EnemyTemporaryCardGrantResult GrantEnemyTemporaryCards(
            EnemyTemporaryCardRequest request,
            EnemyActionTurnContext turnContext = EnemyActionTurnContext.Unspecified)
        {
            try
            {
                return GrantEnemyTemporaryCardsAfter(request, turnContext, ApplyNoEnemyPrimary);
            }
            catch (EnemyTemporaryCardPreparationException exception)
            {
                // Keep the public G3 contract's original exception type and stack.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(exception.InnerException).Throw();
                throw;
            }
        }

        /// <summary>
        /// Prepare all instances, capacity and insertion decisions without changing
        /// zones; then expose the real primary effect before inserting the batch.
        /// Only preparation faults are wrapped. A primary observer fault cannot be
        /// called an unexecuted primary and must not replay its already claimed action.
        /// The reservation never survives this synchronous call. Explicit encounter
        /// cleanup cancels it; health becoming zero alone does not cancel secondary.
        /// </summary>
        internal EnemyTemporaryCardGrantResult GrantEnemyTemporaryCardsAfter(
            EnemyTemporaryCardRequest request, EnemyActionTurnContext turnContext,
            Action applyPrimary)
        {
            PreparedEnemyTemporaryGrant prepared = null;
            try
            {
                try
                {
                    RequireAvailableCardZones();
                    if (applyPrimary == null)
                        throw new ArgumentNullException(nameof(applyPrimary));
                    if (!request.IsConfigured || !request.IsBound)
                        throw new ArgumentException(
                            "敌方临时牌必须先配置并绑定真实卡定义。", nameof(request));
                    if (!string.Equals(request.ContentId, request.Definition.contentId,
                            StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException(
                            "临时牌定义与行为 contentId 不一致。", nameof(request));
                    if (request.MaxRemaining > 0 &&
                        string.IsNullOrWhiteSpace(request.SourceBehaviorContentId))
                        throw new ArgumentException(
                            "受限临时牌必须由来源行为 Profile 绑定。", nameof(request));
                    if (!Enum.IsDefined(typeof(EnemyActionTurnContext), turnContext))
                        throw new ArgumentOutOfRangeException(nameof(turnContext));
                    if (request.Placement ==
                            EncounterTemporaryCardPlacement.HandDuringPlayerTurnElseRandomDrawPile &&
                        turnContext == EnemyActionTurnContext.Unspecified)
                        throw new ArgumentException(
                            "条件临时牌注入必须提供真实回合来源。", nameof(turnContext));

                    EncounterTemporaryCardPlacement placement = request.Placement ==
                        EncounterTemporaryCardPlacement.HandDuringPlayerTurnElseRandomDrawPile
                        ? (turnContext == EnemyActionTurnContext.PlayerTurn
                            ? EncounterTemporaryCardPlacement.Hand
                            : EncounterTemporaryCardPlacement.RandomDrawPile)
                        : request.Placement;
                    if (placement != EncounterTemporaryCardPlacement.Hand &&
                        placement != EncounterTemporaryCardPlacement.RandomDrawPile &&
                        placement != EncounterTemporaryCardPlacement.RandomNonTopDrawPile)
                        throw new ArgumentOutOfRangeException(nameof(request));
                    int count = GetEnemyTemporaryCardCapacity(request);
                    List<CardConfig> destination = placement == EncounterTemporaryCardPlacement.Hand
                        ? _hand : _deck;
                    long finalCount = (long)destination.Count + count;
                    if (finalCount > int.MaxValue)
                        throw new ArgumentOutOfRangeException(nameof(request),
                            "Enemy temporary-card batch exceeds the card-zone index range.");
                    prepared = new PreparedEnemyTemporaryGrant(request, placement, count);
                    _preparedEnemyTemporaryGrant = prepared;
                    // Allocate before primary. Inserts below cannot grow the zone buffer.
                    if (destination.Capacity < finalCount)
                        destination.Capacity = (int)finalCount;
                    IRandomSource preparationRandom = _random;
                    for (int i = 0; i < count && !prepared.Canceled; i++)
                    {
                        CardConfig card = CardInstanceFactory.CreateEncounterTemporary(
                            request.Definition, NextUniqueInstanceId());
                        prepared.Cards.Add(card);
                        card.RuntimeInstance.MarkEnemyInjection(
                            request.ContentId, request.SourceBehaviorContentId);
                        int index = -1;
                        if (placement != EncounterTemporaryCardPlacement.Hand)
                        {
                            int virtualCount = _deck.Count + i;
                            int minimum = placement ==
                                EncounterTemporaryCardPlacement.RandomNonTopDrawPile &&
                                virtualCount > 0 ? 1 : 0;
                            int maximum = virtualCount + 1;
                            // Preserve the legacy non-top empty-pile no-draw case.
                            index = virtualCount == 0 && placement ==
                                EncounterTemporaryCardPlacement.RandomNonTopDrawPile
                                ? 0
                                : (preparationRandom != null
                                    ? preparationRandom.Range(minimum, maximum)
                                    : UnityEngine.Random.Range(minimum, maximum));
                            if (index < minimum || index >= maximum)
                                throw new InvalidOperationException(
                                    "Random source returned an invalid temporary-card insertion index.");
                        }
                        prepared.InsertionIndices.Add(index);
                    }
                }
                catch (Exception exception)
                {
                    throw new EnemyTemporaryCardPreparationException(exception);
                }

                if (!prepared.Canceled) applyPrimary();
                if (prepared.Canceled)
                    return new EnemyTemporaryCardGrantResult(request,
                        new List<CardConfig>(), prepared.Placement, canceledByEncounterEnd: true);
                for (int i = 0; i < prepared.Cards.Count; i++)
                {
                    if (prepared.Placement == EncounterTemporaryCardPlacement.Hand)
                        _hand.Add(prepared.Cards[i]);
                    else
                        _deck.Insert(prepared.InsertionIndices[i], prepared.Cards[i]);
                }
                prepared.Committed = true;
                return new EnemyTemporaryCardGrantResult(
                    request, prepared.Cards, prepared.Placement);
            }
            finally
            {
                if (prepared != null)
                {
                    DestroyUnplacedEnemyCards(prepared);
                    if (ReferenceEquals(_preparedEnemyTemporaryGrant, prepared))
                        _preparedEnemyTemporaryGrant = null;
                }
            }
        }

        /// <summary>Selection checks the same bound payload and encounter-wide capacity as the grant, without modifying zones.</summary>
        public bool CanGrantEnemyTemporaryCards(EnemyTemporaryCardRequest request)
        {
            if (!request.IsConfigured || !request.IsBound ||
                !string.Equals(request.ContentId, request.Definition.contentId,
                    StringComparison.OrdinalIgnoreCase) ||
                (request.MaxRemaining > 0 &&
                 string.IsNullOrWhiteSpace(request.SourceBehaviorContentId)))
                return false;
            return GetEnemyTemporaryCardCapacity(request) > 0;
        }

        private int GetEnemyTemporaryCardCapacity(EnemyTemporaryCardRequest request)
        {
            if (request.MaxRemaining == 0) return request.Count;
            int remaining = CountEnemyTemporaryCards(_deck, request) +
                CountEnemyTemporaryCards(_hand, request) +
                CountEnemyTemporaryCards(_discard, request) +
                CountEnemyTemporaryCards(_resolvingReturns, request);
            return Math.Min(request.Count, Math.Max(0, request.MaxRemaining - remaining));
        }

        private static int CountEnemyTemporaryCards(
            IReadOnlyList<CardConfig> cards,
            EnemyTemporaryCardRequest request)
        {
            int count = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance instance = cards[i]?.RuntimeInstance;
                if (instance != null && instance.Lifetime == CardLifetime.EncounterTemporary &&
                    string.Equals(instance.EnemyInjectedCardContentId, request.ContentId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(instance.EnemySourceBehaviorContentId, request.SourceBehaviorContentId,
                        StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }

        private CardConfig GrantEncounterTemporaryCard(
            CardConfig definition,
            EncounterTemporaryCardPlacement placement)
        {
            RequireAvailableCardZones();
            CardConfig granted = CardInstanceFactory
                .CreateEncounterTemporary(definition, NextUniqueInstanceId());
            switch (placement)
            {
                case EncounterTemporaryCardPlacement.RandomNonTopDrawPile:
                    int insertionIndex = _deck.Count == 0
                        ? 0
                        : RandomRange(1, _deck.Count + 1);
                    _deck.Insert(insertionIndex, granted);
                    break;
                case EncounterTemporaryCardPlacement.RandomDrawPile:
                    _deck.Insert(RandomRange(0, _deck.Count + 1), granted);
                    break;
                case EncounterTemporaryCardPlacement.Hand:
                    _hand.Add(granted);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(placement));
            }
            return granted;
        }

        /// <summary>
        /// 授予一张新卡并立即入手，不受回合抽牌数量限制。
        /// 方法名保留以兼容既有调用方；不再有满手回退分支。
        /// </summary>
        public CardGrantResult GrantToHandOrDrawPile(CardConfig card)
        {
            if (card == null) return default;
            CardConfig granted = CreateNewInstance(card);
            _hand.Add(granted);
            return new CardGrantResult(granted, true);
        }

        /// <summary>
        /// Copy's ordered post-play grant. A consumed/destroyed host is still a valid
        /// source, while a generated Copy is never another source (even if externally
        /// assigned a Copy seal). No draw, payment, shuffle or hand-capacity policy
        /// runs here. All members of this one seal slot enter together before feedback.
        /// </summary>
        internal IReadOnlyList<CardConfig> GrantBaseCopiesToHand(CardConfig source, int count)
        {
            if (source == null || source.runtimeInstanceId <= 0 || count <= 0 ||
                source.RuntimeInstance.IsSealCopy)
                return Array.Empty<CardConfig>();
            // Reject missing original evidence before the allocator retires any ID.
            CardInstanceFactory.RequireKnownAuthorConfigurations(source);
            RequireAvailableCardZones();
            var copies = new List<CardConfig>();
            try
            {
                for (int i = 0; i < count; i++)
                    copies.Add(CardInstanceFactory.CreateBaseTemporaryCopy(source, NextUniqueInstanceId()));
            }
            catch
            {
                // Issued IDs remain retired, but no partial slot or native object
                // leaks if template reconstruction/ID allocation fails.
                foreach (CardConfig card in copies)
                    if (Application.isPlaying) UnityEngine.Object.Destroy(card);
                    else UnityEngine.Object.DestroyImmediate(card);
                throw;
            }
            _hand.AddRange(copies);
            return copies.AsReadOnly();
        }

        /// <summary>
        /// Clears encounter-only cards from draw, hand and discard piles.  This
        /// is intentionally a DeckManager transaction rather than a Dungeon
        /// save filter: all combat consumers see the same post-encounter deck,
        /// and persistent Run snapshots never need to know temporary IDs.
        /// </summary>
        public int RemoveEncounterTemporaryCards()
        {
            // Explicit encounter cleanup cancels the invisible batch.
            // The active scope keeps its identity until finally releases it.
            if (_preparedEnemyTemporaryGrant != null)
                _preparedEnemyTemporaryGrant.Canceled = true;
            int removed = RemoveEncounterTemporaryCards(_deck);
            removed += RemoveEncounterTemporaryCards(_hand);
            removed += RemoveEncounterTemporaryCards(_discard);
            removed += RemoveEncounterTemporaryCards(_resolvingReturns);
            return removed;
        }

        private CardConfig CreateNewInstance(CardConfig card)
        {
            RequireAvailableCardZones();
            return CardInstanceFactory.CreateNew(
                card,
                NextUniqueInstanceId());
        }

        private static int RemoveEncounterTemporaryCards(
            List<CardConfig> cards)
        {
            int before = cards.Count;
            cards.RemoveAll(card => card != null &&
                card.IsEncounterTemporary);
            return before - cards.Count;
        }

        private int NextUniqueInstanceId()
        {
            int candidate;
            do
            {
                candidate = _instanceIds.NextId();
            }
            while (candidate <= 0 || !_issuedInstanceIds.Add(candidate));
            return candidate;
        }

        /// <summary>手牌中是否包含某张牌</summary>
        public bool HasInHand(CardConfig card) => _hand.Contains(card);

        private int CountDrawableCards(List<CardConfig> cards)
        {
            int count = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                if (_canDraw(cards[i]))
                    count++;
            }
            return count;
        }

        private int FindDrawableIndex(
            List<CardConfig> cards,
            int drawableOrdinal)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (!_canDraw(cards[i]))
                    continue;
                if (drawableOrdinal == 0)
                    return i;
                drawableOrdinal--;
            }
            throw new InvalidOperationException(
                "Drawable card count changed during draw selection.");
        }

        private void Shuffle(List<CardConfig> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = RandomRange(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
