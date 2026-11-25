using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum EnemyBehaviorSelectionMode
    {
        FixedSequence = 0,
        WeightedRandom = 1
    }

    /// <summary>
    /// Where an enemy-created encounter-only card enters the player's card
    /// zones. Serialized values are append-only: legacy profiles keep the
    /// non-top draw-pile insertion; G3 may explicitly inject into the hand.
    /// </summary>
    public enum EncounterTemporaryCardPlacement
    {
        RandomNonTopDrawPile = 0,
        Hand = 1,
        // All insertion boundaries are eligible, including the top.
        RandomDrawPile = 2,
        // Uses the owning combat turn, including player-card interrupt batches.
        HandDuringPlayerTurnElseRandomDrawPile = 3
    }

    /// <summary>Execution origin supplied by the combat queue owner; never a second mutable turn state.</summary>
    public enum EnemyActionTurnContext
    {
        Unspecified = 0,
        PlayerTurn = 1,
        OrdinaryEnemyTurn = 2
    }

    /// <summary>
    /// Typed payload for an enemy intent that adds an encounter-only card.
    /// Content ID and count are the authored contract.  Definition is resolved
    /// by Content before a combat starts and is never serialized as Run state.
    /// </summary>
    public readonly struct EnemyTemporaryCardRequest
    {
        public EnemyTemporaryCardRequest(
            string contentId,
            int count,
            EncounterTemporaryCardPlacement placement =
                EncounterTemporaryCardPlacement.RandomNonTopDrawPile,
            CardConfig definition = null,
            int maxRemaining = 0,
            string sourceBehaviorContentId = null)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException(
                    "临时卡必须使用稳定 contentId。", nameof(contentId));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(count), "临时卡数量必须大于 0。");
            if (maxRemaining < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maxRemaining), "临时卡残留上限不能为负；0 表示不限制。");
            if (!Enum.IsDefined(
                    typeof(EncounterTemporaryCardPlacement), placement))
            {
                throw new ArgumentOutOfRangeException(nameof(placement));
            }
            if (definition != null && !string.Equals(
                    contentId,
                    definition.contentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "临时卡定义的 contentId 与行为载荷不一致。",
                    nameof(definition));
            }

            ContentId = contentId.Trim();
            Count = count;
            Placement = placement;
            Definition = definition;
            MaxRemaining = maxRemaining;
            SourceBehaviorContentId = string.IsNullOrWhiteSpace(sourceBehaviorContentId)
                ? null
                : sourceBehaviorContentId.Trim();
        }

        public string ContentId { get; }
        public int Count { get; }
        public EncounterTemporaryCardPlacement Placement { get; }
        /// <summary>Content-bound runtime definition; null until scenario assembly.</summary>
        public CardConfig Definition { get; }
        /// <summary>
        /// Zero preserves legacy unlimited injection. A positive value limits
        /// all surviving injected instances of this card/profile in one deck
        /// encounter, not each enemy, action, turn or destination zone.
        /// </summary>
        public int MaxRemaining { get; }
        /// <summary>Stamped by the owning behavior definition, never an enemy runtime ID.</summary>
        public string SourceBehaviorContentId { get; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ContentId) &&
            Count > 0;
        public bool IsBound => Definition != null;

        public EnemyTemporaryCardRequest WithDefinition(CardConfig definition)
            => new EnemyTemporaryCardRequest(
                ContentId,
                Count,
                Placement,
                definition,
                MaxRemaining,
                SourceBehaviorContentId);
    }

    /// <summary>
    /// Typed secondary payload for one atomic enemy action. Keeping this
    /// contract in Core lets the combat owner apply (for example) armor then
    /// card injection without treating the two effects as separate turns.
    /// </summary>
    public readonly struct EnemyBehaviorCompound
    {
        public EnemyBehaviorCompound(
            EnemyIntentType primaryIntentType,
            EnemyIntentType secondaryIntentType,
            int secondaryAmount,
            string secondaryDescription,
            string secondaryActionId = null,
            string secondaryTemporaryCardContentId = null,
            EncounterTemporaryCardPlacement secondaryTemporaryCardPlacement =
                EncounterTemporaryCardPlacement.RandomNonTopDrawPile,
            CardConfig secondaryTemporaryCardDefinition = null,
            int secondaryTemporaryCardMaxRemaining = 0,
            string secondaryTemporaryCardSourceBehaviorContentId = null)
        {
            if (primaryIntentType != EnemyIntentType.Attack &&
                primaryIntentType != EnemyIntentType.Defend &&
                primaryIntentType != EnemyIntentType.Wait)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(primaryIntentType),
                    "复合行动主行动只能是 Attack、Defend 或 Wait。");
            }
            if (secondaryIntentType != EnemyIntentType.Attack &&
                secondaryIntentType != EnemyIntentType.Defend &&
                secondaryIntentType != EnemyIntentType.Wait &&
                secondaryIntentType != EnemyIntentType.AddEncounterTemporaryCard)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(secondaryIntentType),
                    "复合行动次行动类型无效。");
            }
            if (secondaryIntentType == EnemyIntentType.Wait &&
                secondaryAmount != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(secondaryAmount),
                    "复合 Wait 次行动的数值必须为 0。");
            }
            if (secondaryIntentType != EnemyIntentType.Wait &&
                secondaryAmount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(secondaryAmount),
                    "复合次行动数值必须大于 0。");
            }
            if (string.IsNullOrWhiteSpace(secondaryDescription))
            {
                throw new ArgumentException(
                    "复合次行动描述不能为空。",
                    nameof(secondaryDescription));
            }

            if (secondaryIntentType == EnemyIntentType.AddEncounterTemporaryCard)
            {
                SecondaryTemporaryCard = new EnemyTemporaryCardRequest(
                    secondaryTemporaryCardContentId,
                    secondaryAmount,
                    secondaryTemporaryCardPlacement,
                    secondaryTemporaryCardDefinition,
                    secondaryTemporaryCardMaxRemaining,
                    secondaryTemporaryCardSourceBehaviorContentId);
            }
            else if (!string.IsNullOrWhiteSpace(secondaryTemporaryCardContentId) ||
                     secondaryTemporaryCardDefinition != null ||
                     secondaryTemporaryCardMaxRemaining != 0 ||
                     !string.IsNullOrWhiteSpace(
                         secondaryTemporaryCardSourceBehaviorContentId))
            {
                throw new ArgumentException(
                    "只有复合 AddEncounterTemporaryCard 可以携带临时卡载荷。",
                    nameof(secondaryTemporaryCardContentId));
            }
            else
            {
                SecondaryTemporaryCard = default;
            }

            PrimaryIntentType = primaryIntentType;
            SecondaryIntentType = secondaryIntentType;
            SecondaryAmount = secondaryAmount;
            SecondaryDescription = secondaryDescription;
            SecondaryActionId = string.IsNullOrWhiteSpace(secondaryActionId)
                ? null
                : secondaryActionId.Trim();
        }

        public EnemyIntentType PrimaryIntentType { get; }
        public EnemyIntentType SecondaryIntentType { get; }
        public int SecondaryAmount { get; }
        public string SecondaryDescription { get; }
        public string SecondaryActionId { get; }
        public EnemyTemporaryCardRequest SecondaryTemporaryCard { get; }
        public bool IsConfigured => PrimaryIntentType != EnemyIntentType.None &&
            SecondaryIntentType != EnemyIntentType.None;

        internal EnemyBehaviorCompound WithSecondaryTemporaryCardDefinition(
            CardConfig definition)
        {
            if (SecondaryIntentType != EnemyIntentType.AddEncounterTemporaryCard)
                return this;
            return new EnemyBehaviorCompound(
                PrimaryIntentType,
                SecondaryIntentType,
                SecondaryAmount,
                SecondaryDescription,
                SecondaryActionId,
                SecondaryTemporaryCard.ContentId,
                SecondaryTemporaryCard.Placement,
                definition,
                SecondaryTemporaryCard.MaxRemaining,
                SecondaryTemporaryCard.SourceBehaviorContentId);
        }

        internal EnemyBehaviorCompound WithSecondaryTemporaryCardSourceBehavior(
            string contentId)
        {
            if (SecondaryIntentType != EnemyIntentType.AddEncounterTemporaryCard)
                return this;
            return new EnemyBehaviorCompound(
                PrimaryIntentType,
                SecondaryIntentType,
                SecondaryAmount,
                SecondaryDescription,
                SecondaryActionId,
                SecondaryTemporaryCard.ContentId,
                SecondaryTemporaryCard.Placement,
                SecondaryTemporaryCard.Definition,
                SecondaryTemporaryCard.MaxRemaining,
                contentId);
        }
    }

    /// <summary>
    /// 一次配置化敌人行动。这里只描述 Core 能执行的规则结果，
    /// 不携带 ScriptableObject、动画或音效知识。
    /// </summary>
    public readonly struct EnemyBehaviorAction
    {
        public EnemyBehaviorAction(
            EnemyIntentType intentType,
            int amount,
            string description,
            string actionId = null,
            string temporaryCardContentId = null,
            EncounterTemporaryCardPlacement temporaryCardPlacement =
                EncounterTemporaryCardPlacement.RandomNonTopDrawPile,
            CardConfig temporaryCardDefinition = null,
            int temporaryCardMaxRemaining = 0,
            string temporaryCardSourceBehaviorContentId = null,
            EnemyBehaviorCompound compound = default)
        {
            if (intentType != EnemyIntentType.Attack &&
                intentType != EnemyIntentType.Defend &&
                intentType != EnemyIntentType.Wait &&
                intentType != EnemyIntentType.Escape &&
                intentType != EnemyIntentType.AddEncounterTemporaryCard &&
                intentType != EnemyIntentType.Compound)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(intentType),
                    "配置化行为只支持 Attack、Defend、Wait、Escape、" +
                    "AddEncounterTemporaryCard 与 Compound。");
            }
            bool requiresZeroAmount =
                intentType == EnemyIntentType.Wait ||
                intentType == EnemyIntentType.Escape;
            if (requiresZeroAmount && amount != 0)
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    $"{intentType} 的行动数值必须为 0。");
            if (!requiresZeroAmount && amount <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(amount), "行动数值必须大于 0。");
            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException(
                    "行动描述不能为空。", nameof(description));

            if (intentType == EnemyIntentType.AddEncounterTemporaryCard)
            {
                TemporaryCard = new EnemyTemporaryCardRequest(
                    temporaryCardContentId,
                    amount,
                    temporaryCardPlacement,
                    temporaryCardDefinition,
                    temporaryCardMaxRemaining,
                    temporaryCardSourceBehaviorContentId);
            }
            else if (!string.IsNullOrWhiteSpace(temporaryCardContentId) ||
                     temporaryCardDefinition != null ||
                     temporaryCardMaxRemaining != 0 ||
                     !string.IsNullOrWhiteSpace(temporaryCardSourceBehaviorContentId))
            {
                throw new ArgumentException(
                    "只有 AddEncounterTemporaryCard 可以携带临时卡载荷。",
                    nameof(temporaryCardContentId));
            }
            else
            {
                TemporaryCard = default;
            }

            if (intentType == EnemyIntentType.Compound)
            {
                if (!compound.IsConfigured)
                    throw new ArgumentException(
                        "Compound 必须携带有效的二级行动载荷。",
                        nameof(compound));
                if (compound.PrimaryIntentType != EnemyIntentType.Defend &&
                    compound.PrimaryIntentType != EnemyIntentType.Attack &&
                    compound.PrimaryIntentType != EnemyIntentType.Wait)
                    throw new ArgumentException(
                        "Compound 主行动类型无效。",
                        nameof(compound));
            }
            else if (compound.IsConfigured)
            {
                throw new ArgumentException(
                    "只有 Compound 可以携带复合行动载荷。",
                    nameof(compound));
            }

            IntentType = intentType;
            Amount = amount;
            Description = description;
            ActionId = string.IsNullOrWhiteSpace(actionId) ? null : actionId.Trim();
            Compound = compound;
        }

        public EnemyIntentType IntentType { get; }
        public int Amount { get; }
        public string Description { get; }
        /// <summary>可选的稳定表现/招式 ID；Core 不解释其内容。</summary>
        public string ActionId { get; }
        /// <summary>仅临时卡行为使用的类型化牌区载荷。</summary>
        public EnemyTemporaryCardRequest TemporaryCard { get; }
        public EnemyBehaviorCompound Compound { get; }

        internal EnemyBehaviorAction WithTemporaryCardDefinition(
            CardConfig definition)
        {
            if (IntentType == EnemyIntentType.Compound &&
                Compound.SecondaryIntentType ==
                    EnemyIntentType.AddEncounterTemporaryCard)
            {
                return new EnemyBehaviorAction(
                    IntentType,
                    Amount,
                    Description,
                    ActionId,
                    compound: Compound.WithSecondaryTemporaryCardDefinition(
                        definition));
            }
            if (IntentType != EnemyIntentType.AddEncounterTemporaryCard)
                return this;
            return new EnemyBehaviorAction(
                IntentType,
                Amount,
                Description,
                ActionId,
                TemporaryCard.ContentId,
                TemporaryCard.Placement,
                definition,
                TemporaryCard.MaxRemaining,
                TemporaryCard.SourceBehaviorContentId,
                default);
        }

        internal EnemyBehaviorAction WithTemporaryCardSourceBehavior(string contentId)
        {
            if (IntentType == EnemyIntentType.Compound &&
                Compound.SecondaryIntentType ==
                    EnemyIntentType.AddEncounterTemporaryCard)
            {
                return new EnemyBehaviorAction(
                    IntentType,
                    Amount,
                    Description,
                    ActionId,
                    compound: Compound.WithSecondaryTemporaryCardSourceBehavior(
                        contentId));
            }
            if (IntentType != EnemyIntentType.AddEncounterTemporaryCard)
                return this;
            return new EnemyBehaviorAction(
                IntentType,
                Amount,
                Description,
                ActionId,
                TemporaryCard.ContentId,
                TemporaryCard.Placement,
                TemporaryCard.Definition,
                TemporaryCard.MaxRemaining,
                contentId,
                default);
        }
    }

    public readonly struct EnemyBehaviorChoice
    {
        public EnemyBehaviorChoice(EnemyBehaviorAction action, int weight = 1)
        {
            if (weight <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(weight), "行为权重必须大于 0。");
            Action = action;
            Weight = weight;
        }

        public EnemyBehaviorAction Action { get; }
        public int Weight { get; }
    }

    /// <summary>
    /// 一次行为选择的稳定配置项身份。相邻去重使用 ChoiceIndex，而不是显示名、
    /// 行动类型或伤害值，因此同为攻击的两个招式仍会被视为不同选择。
    /// </summary>
    public readonly struct EnemyBehaviorSelection
    {
        public EnemyBehaviorSelection(
            int choiceIndex,
            EnemyBehaviorAction action)
        {
            ChoiceIndex = choiceIndex;
            Action = action;
        }

        public int ChoiceIndex { get; }
        public EnemyBehaviorAction Action { get; }
    }

    /// <summary>
    /// Core 运行时的敌人行为定义。旧模式仍拥有序列与二阶段定义；健康分组
    /// 仅组合独立不可变定义，由 EnemyHealthBehaviorSelector 唯一拥有选择状态。
    /// 本文件超过审查阈值但仍只拥有行动载荷与定义编译知识；新增选择规则不在此叠加。
    /// </summary>
    public sealed class EnemyBehaviorDefinition
    {
        private readonly EnemyBehaviorChoice[] _choices;
        private readonly int _totalWeight;
        private readonly EnemyBehaviorChoice[] _phaseTwoChoices;
        private readonly int _phaseTwoTotalWeight;
        private readonly int _phaseHealthThresholdPercent;
        private readonly int _phaseActionCountThreshold;

        public EnemyBehaviorDefinition(EnemyHealthBehaviorDefinition healthGroups,
            IReadOnlyList<EnemyDeathActionDefinition> deathActions = null)
        {
            HealthGroups = healthGroups ?? throw new ArgumentNullException(nameof(healthGroups));
            ContentId = healthGroups.ContentId;
            SelectionMode = EnemyBehaviorSelectionMode.WeightedRandom;
            _choices = Array.Empty<EnemyBehaviorChoice>();
            DeathActions = EnemyDeathActionDefinition.Capture(deathActions);
        }

        public EnemyBehaviorDefinition(
            string contentId,
            IReadOnlyList<EnemyBehaviorAction> sequence)
            : this(
                contentId,
                EnemyBehaviorSelectionMode.FixedSequence,
                ToChoices(sequence))
        {
        }

        public EnemyBehaviorDefinition(
            string contentId,
            EnemyBehaviorSelectionMode selectionMode,
            IReadOnlyList<EnemyBehaviorChoice> choices)
            : this(contentId, selectionMode, choices, false)
        {
        }

        public EnemyBehaviorDefinition(
            string contentId,
            EnemyBehaviorSelectionMode selectionMode,
            IReadOnlyList<EnemyBehaviorChoice> choices,
            bool avoidImmediateRepeat)
            : this(
                contentId,
                selectionMode,
                choices,
                avoidImmediateRepeat,
                phaseTwoChoices: null,
                phaseHealthThresholdPercent: 0,
                phaseActionCountThreshold: 0)
        {
        }

        public EnemyBehaviorDefinition(
            string contentId,
            EnemyBehaviorSelectionMode selectionMode,
            IReadOnlyList<EnemyBehaviorChoice> choices,
            bool avoidImmediateRepeat,
            IReadOnlyList<EnemyBehaviorChoice> phaseTwoChoices,
            int phaseHealthThresholdPercent,
            int phaseActionCountThreshold,
            IReadOnlyList<EnemyDeathActionDefinition> deathActions = null)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException(
                    "行为定义必须使用稳定 contentId。", nameof(contentId));
            if (!Enum.IsDefined(typeof(EnemyBehaviorSelectionMode), selectionMode))
                throw new ArgumentOutOfRangeException(nameof(selectionMode));
            if (choices == null || choices.Count == 0)
                throw new ArgumentException(
                    "行为定义至少需要一项行动。", nameof(choices));
            if (avoidImmediateRepeat &&
                selectionMode != EnemyBehaviorSelectionMode.WeightedRandom)
            {
                throw new ArgumentException(
                    "相邻去重只适用于加权随机行为。",
                    nameof(avoidImmediateRepeat));
            }
            bool hasPhaseTwo = phaseTwoChoices != null;
            if (hasPhaseTwo && phaseTwoChoices.Count == 0)
                throw new ArgumentException(
                    "第二行为组至少需要一项行动。",
                    nameof(phaseTwoChoices));
            if (hasPhaseTwo &&
                (phaseHealthThresholdPercent < 1 ||
                 phaseHealthThresholdPercent > 99))
                throw new ArgumentOutOfRangeException(
                    nameof(phaseHealthThresholdPercent),
                    "阶段生命阈值必须在 1 到 99 之间。");
            if (hasPhaseTwo && phaseActionCountThreshold <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(phaseActionCountThreshold),
                    "阶段行动阈值必须大于 0。");

            ContentId = contentId.Trim();
            DeathActions = EnemyDeathActionDefinition.Capture(deathActions);
            SelectionMode = selectionMode;
            AvoidImmediateRepeat = avoidImmediateRepeat;
            Dictionary<string, int> temporaryCardLimits;
            int totalWeight;
            _choices = BuildChoices(
                choices,
                ContentId,
                out totalWeight,
                out temporaryCardLimits);
            _totalWeight = totalWeight;
            Dictionary<string, int> phaseTwoLimits = null;
            int phaseTwoTotalWeight = 0;
            _phaseTwoChoices = hasPhaseTwo
                ? BuildChoices(
                    phaseTwoChoices,
                    ContentId,
                    out phaseTwoTotalWeight,
                    out phaseTwoLimits)
                : null;
            _phaseTwoTotalWeight = phaseTwoTotalWeight;
            if (hasPhaseTwo)
            {
                foreach (KeyValuePair<string, int> pair in phaseTwoLimits)
                {
                    if (temporaryCardLimits.TryGetValue(
                            pair.Key,
                            out int existing) &&
                        existing != pair.Value)
                    {
                        throw new ArgumentException(
                            $"{ContentId} 同卡ID的 temporaryCardMaxRemaining 必须一致。",
                            nameof(phaseTwoChoices));
                    }
                    temporaryCardLimits[pair.Key] = pair.Value;
                }
            }
            _phaseHealthThresholdPercent = hasPhaseTwo
                ? phaseHealthThresholdPercent
                : 0;
            _phaseActionCountThreshold = hasPhaseTwo
                ? phaseActionCountThreshold
                : 0;
        }

        private static EnemyBehaviorChoice[] BuildChoices(
            IReadOnlyList<EnemyBehaviorChoice> source,
            string contentId,
            out int totalWeight,
            out Dictionary<string, int> temporaryCardLimits)
        {
            if (source == null || source.Count == 0)
                throw new ArgumentException(
                    "行为定义至少需要一项行动。",
                    nameof(source));
            var result = new EnemyBehaviorChoice[source.Count];
            temporaryCardLimits = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            long total = 0;
            for (int i = 0; i < source.Count; i++)
            {
                result[i] = new EnemyBehaviorChoice(
                    source[i].Action.WithTemporaryCardSourceBehavior(contentId),
                    source[i].Weight);
                EnemyBehaviorAction action = result[i].Action;
                RegisterTemporaryCardLimit(
                    temporaryCardLimits,
                    action.TemporaryCard,
                    contentId);
                if (action.IntentType == EnemyIntentType.Compound)
                    RegisterTemporaryCardLimit(
                        temporaryCardLimits,
                        action.Compound.SecondaryTemporaryCard,
                        contentId);
                total += source[i].Weight;
                if (total > int.MaxValue)
                    throw new ArgumentOutOfRangeException(
                        nameof(source),
                        "行为总权重不能超过 Int32.MaxValue。");
            }
            totalWeight = (int)total;
            return result;
        }

        private static void RegisterTemporaryCardLimit(
            Dictionary<string, int> limits,
            EnemyTemporaryCardRequest temporary,
            string contentId)
        {
            if (!temporary.IsConfigured)
                return;
            if (limits.TryGetValue(
                    temporary.ContentId,
                    out int existing) &&
                existing != temporary.MaxRemaining)
            {
                throw new ArgumentException(
                    $"{contentId} 同卡ID的 temporaryCardMaxRemaining 必须一致。");
            }
            limits[temporary.ContentId] = temporary.MaxRemaining;
        }

        public string ContentId { get; }
        public EnemyHealthBehaviorDefinition HealthGroups { get; }
        public IReadOnlyList<EnemyDeathActionDefinition> DeathActions { get; }
        public EnemyBehaviorSelectionMode SelectionMode { get; }
        public bool AvoidImmediateRepeat { get; }
        public int ActionCount
        {
            get
            {
                RequireLegacyMode();
                return _choices.Length;
            }
        }
        public bool HasPhaseSwitch => _phaseTwoChoices != null;
        public int PhaseTwoActionCount => _phaseTwoChoices?.Length ?? 0;

        /// <summary>Content-only snapshot hook used while compiling a second authored group.</summary>
        public IReadOnlyList<EnemyBehaviorChoice> GetChoicesForAuthoring()
        {
            RequireLegacyMode();
            return _choices;
        }

        /// <summary>Enumerates every authored group without projecting health groups into legacy phases.</summary>
        public IEnumerable<EnemyBehaviorAction> EnumerateActions()
        {
            if (HealthGroups != null)
            {
                foreach (EnemyBehaviorAction action in HealthGroups.EnumerateActions())
                    yield return action;
                yield break;
            }
            foreach (EnemyBehaviorChoice choice in _choices)
                yield return choice.Action;
            if (_phaseTwoChoices != null)
                foreach (EnemyBehaviorChoice choice in _phaseTwoChoices)
                    yield return choice.Action;
        }

        /// <summary>
        /// Evaluates the one-way phase condition immediately before an enemy
        /// selects its next legal action.  Stage waiting enemies never call
        /// this method because the AI only plans deployed actors.
        /// </summary>
        public bool TryEnterPhaseTwo(EnemyStats enemy)
        {
            RequireLegacyMode();
            if (!HasPhaseSwitch || enemy == null ||
                enemy.behaviorPhaseIndex != 0)
                return false;
            int maximumHealth = Math.Max(1, enemy.baseHealth);
            bool healthReached =
                (long)Math.Max(0, enemy.currentHealth) * 100L <=
                (long)maximumHealth * _phaseHealthThresholdPercent;
            bool actionReached =
                enemy.behaviorLegalActionCount >= _phaseActionCountThreshold - 1;
            if (!healthReached && !actionReached)
                return false;
            enemy.behaviorPhaseIndex = 1;
            enemy.behaviorActionIndex = 0;
            return true;
        }

        /// <summary>
        /// Content binds authored stable temporary-card IDs to per-encounter
        /// runtime definitions here.  The cached behavior profile remains
        /// immutable; each enemy lineup receives its own bound Core definition.
        /// </summary>
        public EnemyBehaviorDefinition BindTemporaryCardDefinitions(
            Func<string, CardConfig> resolver)
        {
            if (HealthGroups != null)
            {
                EnemyHealthBehaviorDefinition bound = HealthGroups.BindTemporaryCardDefinitions(resolver);
                return ReferenceEquals(bound, HealthGroups) ? this : new EnemyBehaviorDefinition(bound, DeathActions);
            }
            bool needsBinding = false;
            for (int i = 0; i < _choices.Length; i++)
            {
                if (_choices[i].Action.IntentType ==
                        EnemyIntentType.AddEncounterTemporaryCard ||
                    (_choices[i].Action.IntentType ==
                        EnemyIntentType.Compound &&
                     _choices[i].Action.Compound.SecondaryIntentType ==
                        EnemyIntentType.AddEncounterTemporaryCard))
                {
                    needsBinding = true;
                    break;
                }
            }
            if (!needsBinding && _phaseTwoChoices != null)
            {
                for (int i = 0; i < _phaseTwoChoices.Length; i++)
                {
                    if (_phaseTwoChoices[i].Action.IntentType ==
                            EnemyIntentType.AddEncounterTemporaryCard ||
                        (_phaseTwoChoices[i].Action.IntentType ==
                            EnemyIntentType.Compound &&
                         _phaseTwoChoices[i].Action.Compound.SecondaryIntentType ==
                            EnemyIntentType.AddEncounterTemporaryCard))
                    {
                        needsBinding = true;
                        break;
                    }
                }
            }
            if (!needsBinding)
                return this;
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            EnemyBehaviorChoice[] boundChoices =
                BindChoices(_choices, resolver);
            EnemyBehaviorChoice[] boundPhaseTwoChoices =
                _phaseTwoChoices == null
                    ? null
                    : BindChoices(_phaseTwoChoices, resolver);

            return new EnemyBehaviorDefinition(
                ContentId,
                SelectionMode,
                boundChoices,
                AvoidImmediateRepeat,
                boundPhaseTwoChoices,
                _phaseHealthThresholdPercent,
                _phaseActionCountThreshold,
                DeathActions);
        }

        private EnemyBehaviorChoice[] BindChoices(
            IReadOnlyList<EnemyBehaviorChoice> source,
            Func<string, CardConfig> resolver)
        {
            var result = new EnemyBehaviorChoice[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                EnemyBehaviorChoice choice = source[i];
                EnemyBehaviorAction action = choice.Action;
                if (action.IntentType == EnemyIntentType.AddEncounterTemporaryCard ||
                    (action.IntentType == EnemyIntentType.Compound &&
                     action.Compound.SecondaryIntentType ==
                        EnemyIntentType.AddEncounterTemporaryCard))
                {
                    string cardId = action.IntentType ==
                            EnemyIntentType.Compound
                        ? action.Compound.SecondaryTemporaryCard.ContentId
                        : action.TemporaryCard.ContentId;
                    CardConfig definition = resolver(cardId);
                    if (definition == null)
                    {
                        throw new InvalidOperationException(
                            $"敌人行为 {ContentId} 的临时卡不存在：{cardId}。");
                    }
                    action = action.WithTemporaryCardDefinition(definition);
                }
                result[i] = new EnemyBehaviorChoice(action, choice.Weight);
            }
            return result;
        }

        public EnemyBehaviorAction GetAction(
            int actionIndex,
            int phaseIndex = 0)
        {
            EnemyBehaviorChoice[] choices = GetChoices(phaseIndex);
            int normalized = NormalizeIndex(actionIndex, choices.Length);
            return choices[normalized].Action;
        }

        public int GetWeight(
            int actionIndex,
            int phaseIndex = 0)
        {
            EnemyBehaviorChoice[] choices = GetChoices(phaseIndex);
            return choices[NormalizeIndex(actionIndex, choices.Length)].Weight;
        }

        /// <summary>
        /// 选择本回合行动。固定序列不消费随机数；加权模式恰好消费一次整数
        /// 随机值，使预告、执行和固定 seed 模拟共享同一选择结果。
        /// </summary>
        public EnemyBehaviorAction SelectAction(
            int actionIndex,
            Func<int, int, int> randomRange)
            => Select(
                actionIndex,
                excludedChoiceIndex: -1,
                randomRange,
                phaseIndex: 0).Action;

        /// <summary>
        /// 选择行动并返回配置项身份。只有显式启用相邻去重的加权行为才会排除
        /// excludedActionId 对应的同招；旧无 ID 数据回退 excludedChoiceIndex。
        /// 排除后对剩余权重一次归一化，仍恰好消费一次随机值。
        /// 单一选项或所有选项同招、没有替代项时会安全地允许重复。
        /// </summary>
        public EnemyBehaviorSelection Select(
            int actionIndex,
            int excludedChoiceIndex,
            Func<int, int, int> randomRange,
            int phaseIndex = 0,
            string excludedActionId = null)
            => Select(actionIndex, randomRange, phaseIndex,
                AvoidImmediateRepeat
                    ? new EnemyActionRepeatConstraint(false, excludedActionId, phaseIndex, excludedChoiceIndex)
                    : default);

        /// <summary>Runtime selection consumes one encounter-owned policy snapshot;
        /// Stage policy is independent of this author's opt-in flag.</summary>
        internal EnemyBehaviorSelection Select(int actionIndex, Func<int, int, int> randomRange,
            int phaseIndex, EnemyActionRepeatConstraint repeat)
        {
            EnemyBehaviorChoice[] choices = GetChoices(phaseIndex);
            int totalWeight = phaseIndex == 1
                ? _phaseTwoTotalWeight
                : _totalWeight;
            if (SelectionMode == EnemyBehaviorSelectionMode.FixedSequence)
            {
                int fixedIndex = NormalizeIndex(actionIndex, choices.Length);
                if (repeat.StageMode)
                    for (int offset = 0; offset < choices.Length; offset++)
                    {
                        int candidate = (fixedIndex + offset) % choices.Length;
                        if (repeat.Excludes(choices[candidate].Action.ActionId, candidate, phaseIndex)) continue;
                        return new EnemyBehaviorSelection(candidate, choices[candidate].Action);
                    }
                return new EnemyBehaviorSelection(
                    fixedIndex,
                    choices[fixedIndex].Action);
            }
            if (randomRange == null)
                throw new ArgumentNullException(nameof(randomRange));

            int selectableWeight = totalWeight;
            for (int i = 0; i < choices.Length; i++)
                if (repeat.Excludes(choices[i].Action.ActionId, i, phaseIndex))
                    selectableWeight -= choices[i].Weight;
            bool applyExclusion = selectableWeight > 0;
            if (selectableWeight <= 0)
            {
                selectableWeight = totalWeight;
            }

            int roll = randomRange(0, selectableWeight);
            int cumulative = 0;
            for (int i = 0; i < choices.Length; i++)
            {
                if (applyExclusion && repeat.Excludes(choices[i].Action.ActionId, i, phaseIndex))
                    continue;
                cumulative += choices[i].Weight;
                if (roll < cumulative)
                {
                    return new EnemyBehaviorSelection(
                        i,
                        choices[i].Action);
                }
            }

            for (int i = choices.Length - 1; i >= 0; i--)
            {
                if (!applyExclusion || !repeat.Excludes(choices[i].Action.ActionId, i, phaseIndex))
                {
                    return new EnemyBehaviorSelection(
                        i,
                        choices[i].Action);
                }
            }
            return new EnemyBehaviorSelection(0, choices[0].Action);
        }

        public int Advance(int actionIndex, int phaseIndex = 0)
            => SelectionMode == EnemyBehaviorSelectionMode.FixedSequence
                ? NormalizeIndex(
                    actionIndex + 1,
                    GetChoices(phaseIndex).Length)
                : NormalizeIndex(
                    actionIndex,
                    GetChoices(phaseIndex).Length);

        private EnemyBehaviorChoice[] GetChoices(int phaseIndex)
        {
            RequireLegacyMode();
            return phaseIndex == 1 && _phaseTwoChoices != null ? _phaseTwoChoices : _choices;
        }

        private void RequireLegacyMode()
        {
            if (HealthGroups != null)
                throw new InvalidOperationException("Health-group behaviors require EnemyHealthBehaviorSelector; legacy selection is unavailable.");
        }

        private static int NormalizeIndex(int actionIndex, int length)
        {
            int value = actionIndex % length;
            return value < 0 ? value + length : value;
        }

        private static IReadOnlyList<EnemyBehaviorChoice> ToChoices(
            IReadOnlyList<EnemyBehaviorAction> sequence)
        {
            if (sequence == null)
                return null;
            var choices = new EnemyBehaviorChoice[sequence.Count];
            for (int i = 0; i < sequence.Count; i++)
                choices[i] = new EnemyBehaviorChoice(sequence[i]);
            return choices;
        }
    }
}
