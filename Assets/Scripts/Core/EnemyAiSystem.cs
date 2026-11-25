using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 敌人意图类型。
    /// </summary>
    public enum EnemyIntentType
    {
        None,       // 无行动
        Attack,     // 攻击玩家
        Defend,     // 获得护甲
        Buff,       // 自我强化
        Debuff,     // 对玩家施加负面效果
        Summon,     // 召唤增援
        Special,    // 特殊技能
        Wait,       // 明确等待；会正常结算但不产生战斗数值
        Escape,     // 从第一排撤离战斗；不视为死亡或击败
        /// <summary>
        /// Adds one or more resolved encounter-only cards to the player's draw
        /// pile.  The payload owns stable content ID/count/placement; it is not
        /// a disguised Wait or an inferred card effect.
        /// </summary>
        AddEncounterTemporaryCard,
        /// <summary>
        /// A single atomic enemy action containing a primary utility/attack
        /// and a typed secondary action.  The combat owner resolves both
        /// parts as one command; it is not two independently planned turns.
        /// </summary>
        Compound
    }

    /// <summary>缴械使用的高层行动分类；移动和功能行动不会消耗缴械。</summary>
    public enum EnemyActionType
    {
        None,
        Attack,
        Utility,
        Movement
    }

    /// <summary>
    /// 敌人意图。描述"这个敌人本回合打算做什么"。
    /// </summary>
    public struct EnemyIntent
    {
        public EnemyIntentType Type;
        public int Amount;      // 攻击伤害 / 护甲量 / 属性值
        public int TargetCount; // 多目标数量（默认 1）
        public string Description;
        /// <summary>配置化行动携带的稳定招式 ID，供表现层选择动作变体。</summary>
        public string ActionId;
        public EnemyActionType ActionType;
        public bool CanBeDisarmed;
        /// <summary>Only populated by AddEncounterTemporaryCard.</summary>
        public EnemyTemporaryCardRequest TemporaryCard;
        /// <summary>Typed secondary payload used only by Compound.</summary>
        public EnemyIntentType SecondaryType;
        public EnemyIntentType PrimaryType;
        public int SecondaryAmount;
        public string SecondaryDescription;
        public string SecondaryActionId;
        public EnemyTemporaryCardRequest SecondaryTemporaryCard;

        public static EnemyIntent None => new EnemyIntent { Type = EnemyIntentType.None };

        public static EnemyIntent Attack(
            int damage,
            string description = null,
            string actionId = null) =>
            new EnemyIntent
            {
                Type = EnemyIntentType.Attack,
                Amount = damage,
                TargetCount = 1,
                ActionType = EnemyActionType.Attack,
                CanBeDisarmed = true,
                Description = string.IsNullOrWhiteSpace(description)
                    ? $"攻击 {damage}"
                    : description,
                ActionId = string.IsNullOrWhiteSpace(actionId) ? null : actionId
            };

        public static EnemyIntent Defend(
            int armor,
            string description = null,
            string actionId = null) =>
            new EnemyIntent
            {
                Type = EnemyIntentType.Defend,
                Amount = armor,
                ActionType = EnemyActionType.Utility,
                Description = string.IsNullOrWhiteSpace(description)
                    ? $"防御 +{armor}"
                    : description,
                ActionId = string.IsNullOrWhiteSpace(actionId) ? null : actionId
            };

        public static EnemyIntent Wait(
            string description = null,
            string actionId = null) =>
            new EnemyIntent
            {
                Type = EnemyIntentType.Wait,
                Amount = 0,
                ActionType = EnemyActionType.Utility,
                Description = string.IsNullOrWhiteSpace(description)
                    ? "等待"
                    : description,
                ActionId = string.IsNullOrWhiteSpace(actionId) ? null : actionId
            };

        public static EnemyIntent Escape(
            string description = null,
            string actionId = null) =>
            new EnemyIntent
            {
                Type = EnemyIntentType.Escape,
                Amount = 0,
                ActionType = EnemyActionType.Movement,
                Description = string.IsNullOrWhiteSpace(description)
                    ? "逃离战场"
                    : description,
                ActionId = string.IsNullOrWhiteSpace(actionId)
                    ? null
                    : actionId
            };

        public static EnemyIntent AddEncounterTemporaryCard(
            EnemyTemporaryCardRequest temporaryCard,
            string description = null,
            string actionId = null)
        {
            if (!temporaryCard.IsConfigured)
                throw new ArgumentException(
                    "临时卡敌方意图必须携带有效载荷。",
                    nameof(temporaryCard));
            return new EnemyIntent
            {
                Type = EnemyIntentType.AddEncounterTemporaryCard,
                Amount = temporaryCard.Count,
                TargetCount = 0,
                ActionType = EnemyActionType.Utility,
                CanBeDisarmed = false,
                Description = string.IsNullOrWhiteSpace(description)
                    ? $"加入临时卡 {temporaryCard.ContentId} ×{temporaryCard.Count}"
                    : description,
                ActionId = string.IsNullOrWhiteSpace(actionId)
                    ? null
                    : actionId,
                TemporaryCard = temporaryCard
            };
        }

        public static EnemyIntent Compound(
            EnemyIntent primary,
            EnemyIntent secondary,
            string description = null,
            string actionId = null)
        {
            if (primary.IsNone || secondary.IsNone)
                throw new ArgumentException(
                    "复合敌方意图必须包含两个有效子行动。",
                    nameof(primary));
            if (secondary.Type == EnemyIntentType.Compound)
                throw new ArgumentException(
                    "复合敌方意图不允许嵌套 Compound。",
                    nameof(secondary));
            bool primaryAttacks = primary.Type == EnemyIntentType.Attack ||
                primary.ActionType == EnemyActionType.Attack;
            bool secondaryAttacks = secondary.Type == EnemyIntentType.Attack ||
                secondary.ActionType == EnemyActionType.Attack;
            return new EnemyIntent
            {
                Type = EnemyIntentType.Compound,
                PrimaryType = primary.Type,
                Amount = primary.Amount,
                TargetCount = primary.TargetCount,
                // A compound's physical admission and cancellation cover the
                // whole choice, including an attack after armor or another utility.
                // Type covers hand-built Attack payloads with a default category;
                // the explicit category preserves the existing legacy contract.
                ActionType = primaryAttacks || secondaryAttacks
                    ? EnemyActionType.Attack
                    : EnemyActionType.Utility,
                CanBeDisarmed = (primaryAttacks && primary.CanBeDisarmed) ||
                    (secondaryAttacks && secondary.CanBeDisarmed),
                Description = string.IsNullOrWhiteSpace(description)
                    ? $"{primary.Description}；{secondary.Description}"
                    : description,
                ActionId = string.IsNullOrWhiteSpace(actionId)
                    ? primary.ActionId
                    : actionId,
                TemporaryCard = primary.TemporaryCard,
                SecondaryType = secondary.Type,
                SecondaryAmount = secondary.Amount,
                SecondaryDescription = secondary.Description,
                SecondaryActionId = secondary.ActionId,
                SecondaryTemporaryCard = secondary.TemporaryCard
            };
        }

        public bool IsNone => Type == EnemyIntentType.None;
    }

    /// <summary>
    /// 敌人行动指令。由 EnemyAiSystem 生成，CombatController 执行。
    /// </summary>
    public struct EnemyActionCommand : ICombatCommand
    {
        public string CommandType => "EnemyAction";
        public int EnemyIndex;
        public EnemyIntent Intent;
        public EnemyActionReceipt Receipt { get; }

        public EnemyActionCommand(int enemyIndex, EnemyIntent intent)
        {
            EnemyIndex = enemyIndex;
            Intent = intent;
            Receipt = null;
        }

        public EnemyActionCommand(EnemyActionReceipt receipt)
        {
            Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
            EnemyIndex = receipt.EnemyIndex;
            Intent = receipt.Intent;
        }
    }

    /// <summary>
    /// 敌人 AI 系统。负责为每个存活敌人选择意图，并生成行动指令。
    /// <para>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>AI 只负责选择意图和生成指令，不直接操作 UI。</item>
    /// <item>指令通过 <see cref="CombatController.ExecuteCommand"/> 执行。</item>
    /// <item>选择过程使用 <see cref="CombatState.Random"/> 保证可复现。</item>
    /// <item>意图应尽量配置化，当前为简单随机策略，后续可扩展。</item>
    /// </list>
    /// </para>
    /// 复杂度复审：预告、已结算选择和合法行动计数共同维护行为游标，
    /// 本次修复仍在该职责内；新增独立选择策略时应先复审封装边界。
    /// 原牌意图取消仍属于冻结预告的同一生命周期：本类唯一持有本回合取消集合，
    /// 查询和指令生成共用投影；新回合 RefreshIntents 清除，不推进未执行的行为游标。
    /// 批次回执的发放、认领和成功完成也由本类唯一拥有；认领先于同步副作用，
    /// 只有成功完成才计数，失败认领不可重试，刷新/取消不打断在执行栈内的认领。
    /// 本类不拥有计时或执行队列；增加独立职责前须重新审查此封装边界。
    /// 配置到意图的投影保留所有已支持行动的稳定招式 ID，包括纯功能行动和复合次行动；
    /// 此修复不增加状态、计时策略或执行职责。
    /// 相邻去重规则与成功记忆归 EnemyActionRepeatPolicy，本类只在
    /// 既有选择/成功回执边界提交事实；不按关卡保存另一套避重状态。
    /// </summary>
    public class EnemyAiSystem
    {
        private readonly CombatState _state;
        private List<EnemyIntent> _plannedIntents;
        // Preview replacement cannot revoke a batch already handed to the queue.
        // Uncaptured old previews are released; captured receipts live until consumed/cancelled.
        private readonly Dictionary<int, EnemyActionReceipt> _previewReceipts =
            new Dictionary<int, EnemyActionReceipt>();
        private readonly HashSet<EnemyActionReceipt> _outstandingReceipts =
            new HashSet<EnemyActionReceipt>();
        private readonly HashSet<EnemyActionReceipt> _claimedReceipts =
            new HashSet<EnemyActionReceipt>();
        private readonly EnemyActionRepeatPolicy _actionRepeat = new EnemyActionRepeatPolicy();
        private readonly HashSet<int> _removedIntentRuntimeIds = new HashSet<int>();
        private readonly Dictionary<int, EnemyHealthBehaviorSelector> _healthSelectors =
            new Dictionary<int, EnemyHealthBehaviorSelector>();

        public EnemyAiSystem(CombatState state)
        {
            _state = state;
            foreach (EnemyStats enemy in state.Enemies)
                if (enemy?.behaviorDefinition?.HealthGroups != null)
                    GetHealthSelector(enemy);
        }

        /// <summary>Observation reads selection memory without creating a selector, evaluating health or drawing randomness.</summary>
        public bool TryGetHealthBehaviorSnapshot(int runtimeId, out EnemyHealthBehaviorSnapshot snapshot)
        {
            snapshot = default;
            if (!_healthSelectors.TryGetValue(runtimeId, out EnemyHealthBehaviorSelector selector))
                return false;
            foreach (EnemyStats enemy in _state.Enemies)
                if (enemy != null && enemy.runtimeId == runtimeId &&
                    ReferenceEquals(selector.Definition, enemy.behaviorDefinition?.HealthGroups))
                {
                    snapshot = selector.Snapshot;
                    return true;
                }
            return false;
        }

        private EnemyHealthBehaviorSelector GetHealthSelector(EnemyStats enemy)
        {
            EnemyHealthBehaviorDefinition definition = enemy.behaviorDefinition.HealthGroups;
            if (!_healthSelectors.TryGetValue(enemy.runtimeId, out EnemyHealthBehaviorSelector selector) ||
                !ReferenceEquals(selector.Definition, definition))
            {
                selector = new EnemyHealthBehaviorSelector(definition, enemy.currentHealth, enemy.baseHealth);
                _healthSelectors[enemy.runtimeId] = selector;
                enemy.behaviorPhaseIndex = selector.Snapshot.CurrentPhaseIndex;
            }
            return selector;
        }

        /// <summary>
        /// 生成并冻结本轮敌人意图。UI、目标选择和敌人行动必须读取同一份快照，
        /// 避免查看意图或伤害预览时再次消耗随机数。
        /// </summary>
        public IReadOnlyList<EnemyIntent> RefreshIntents()
        {
            // Registration precedes initial AI preview construction. The first
            // preview must exist even while normal-turn selection is globally gated.
            if (_plannedIntents != null && _plannedIntents.Count == _state.Enemies.Count &&
                _state.EnemyInterrupts?.HasFrontInterrupts == true)
                return ReconcilePlannedIntents();
            return RefreshInterruptIntents();
        }

        /// <summary>Force the next preview after each interrupt batch; old batch receipts remain independent.</summary>
        public IReadOnlyList<EnemyIntent> RefreshInterruptIntents()
        {
            _removedIntentRuntimeIds.Clear();
            _plannedIntents = BuildIntents();
            return _plannedIntents;
        }

        /// <summary>
        /// 从已有预告里随机保留至多 count 个候选，再逐个按敌人抗性判定。
        /// 不创建或重新 Roll 意图，不依赖伤害命中或攻击行，取消复合意图的全部行动。
        /// count 不受 Combo/Amount 放大；失败不重新挑选另一名敌人。
        /// </summary>
        public int RemoveRandomPlannedIntents(int count)
            => RemoveRandomPlannedIntents(count, attackIntentOnly: false,
                removeAllIntents: false);

        /// <summary>
        /// 从冻结预告中随机选取至多 count 个候选，再按抗性取消。
        /// attackIntentOnly 对齐 VC RemoveIntentEffect 的 EnemyActionDamage
        /// 过滤，只让攻击行动进入候选；失败不补选。
        /// </summary>
        public int RemoveRandomPlannedIntents(int count, bool attackIntentOnly)
            => RemoveRandomPlannedIntents(count, attackIntentOnly,
                removeAllIntents: false);

        /// <summary>
        /// 从冻结预告中选取候选并按抗性取消。VC Garlic 的
        /// removeAllIntents=true 会跳过 EnemyActionDamage 类型过滤；仍由 count
        /// 限制随机候选数量。它不是“清空所有敌人意图”的数量开关。
        /// </summary>
        public int RemoveRandomPlannedIntents(
            int count,
            bool attackIntentOnly,
            bool removeAllIntents)
        {
            if (count <= 0) return 0;
            IReadOnlyList<EnemyIntent> intents = GetPlannedIntents();
            var candidates = new List<int>();
            for (int i = 0; i < intents.Count; i++)
                if (!intents[i].IsNone &&
                    !(removeAllIntents && IsWaitOnly(intents[i])) &&
                    (!attackIntentOnly || removeAllIntents ||
                     (intents[i].ActionType == EnemyActionType.Attack &&
                      intents[i].CanBeDisarmed)))
                    candidates.Add(i);
            // VC RemoveIntentEffect 的 _count 仍是候选预算；removeAllIntents
            // 只控制是否跳过 actionToRemove 过滤，不改变随机抽样数量。
            while (candidates.Count > count)
                candidates.RemoveAt(_state.RandomRange(0, candidates.Count));
            int removed = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                int target = candidates[i];
                EnemyStats enemy = _state.Enemies[target];
                float chance = UnityEngine.Mathf.Clamp01(enemy.EffectiveDisarmMultiplier);
                if (_state.RandomValue() >= chance) continue;
                _removedIntentRuntimeIds.Add(enemy.runtimeId);
                _plannedIntents[target] = EnemyIntent.None;
                _previewReceipts.Remove(enemy.runtimeId);
                _outstandingReceipts.RemoveWhere(receipt => receipt.EnemyRuntimeId == enemy.runtimeId);
                if (enemy.behaviorDefinition?.HealthGroups != null &&
                    _healthSelectors.TryGetValue(enemy.runtimeId, out EnemyHealthBehaviorSelector selector))
                    selector.ClearCurrentChoice();
                removed++;
            }
            return removed;
        }

        private static bool IsWaitOnly(EnemyIntent intent)
        {
            // Native RemoveIntentEffect skips a pure wait intent. A compound
            // action remains eligible when it carries any non-wait sub-action.
            return intent.Type == EnemyIntentType.Wait;
        }

        /// <summary>已取消的本回合行动不能由旧命令或缓存预告重新执行。</summary>
        public bool IsIntentRemoved(int enemyIndex)
            => enemyIndex >= 0 && enemyIndex < _state.Enemies.Count &&
               _state.Enemies[enemyIndex] != null &&
               _removedIntentRuntimeIds.Contains(_state.Enemies[enemyIndex].runtimeId);

        /// <summary>
        /// 返回当前意图快照。尚未生成时只生成一次；随后根据死亡、击退和补位
        /// 修正为当前真正会执行的行动，但不会重新 Roll 原有前排敌人的意图。
        /// </summary>
        public List<EnemyIntent> GenerateIntents()
        {
            if (_plannedIntents == null || _plannedIntents.Count != _state.Enemies.Count)
                _plannedIntents = BuildIntents();
            return ReconcilePlannedIntents();
        }

        /// <summary>
        /// Returns the already-frozen intent snapshot without generating or
        /// rerolling anything. Observation and preview code must use this query
        /// so merely inspecting battle state never advances the random stream.
        /// </summary>
        public IReadOnlyList<EnemyIntent> GetPlannedIntents()
        {
            if (_plannedIntents == null ||
                _plannedIntents.Count != _state.Enemies.Count)
            {
                return Array.Empty<EnemyIntent>();
            }
            return ReconcilePlannedIntents();
        }

        /// <summary>
        /// 生成意图。阵型平时静止：“每列最前”的存活敌人才会规划行动；
        /// 其中攻击与撤离意图只允许第一排保留。刚补位
        /// （justFilledFrontRow）的敌人本回合不出手。
        /// </summary>
        private List<EnemyIntent> BuildIntents()
        {
            _previewReceipts.Clear();
            var intents = new List<EnemyIntent>();
            for (int i = 0; i < _state.Enemies.Count; i++)
            {
                var enemy = _state.Enemies[i];
                if (enemy == null || enemy.IsDead || !enemy.IsDeployed ||
                    !EnemyFormationLayout.IsFullyInsideVisibleRows(enemy) ||
                    !EnemyFormationLayout.IsColumnFront(_state.Enemies, i) ||
                    enemy.justFilledFrontRow || IsIntentRemoved(i))
                {
                    intents.Add(EnemyIntent.None);
                    continue;
                }
                EnemyIntent intent = ApplyActionRange(enemy, GenerateIntent(enemy, out int choiceIndex));
                intents.Add(intent);
                if (!intent.IsNone)
                    _previewReceipts[enemy.runtimeId] = new EnemyActionReceipt(i, enemy,
                        enemy.behaviorDefinition, choiceIndex, enemy.behaviorPhaseIndex,
                        enemy.behaviorActionIndex, intent);
            }
            return intents;
        }

        /// <summary>
        /// 把已规划的意图修正为当前真正会执行的行动：死亡或退到非列前的敌人
        /// 变为 None；非第一排不能保留攻击或撤离意图；刚补位的敌人本回合
        /// 不出手；其余保留原规划，不重新 Roll。
        /// </summary>
        private List<EnemyIntent> ReconcilePlannedIntents()
        {
            var intents = new List<EnemyIntent>(_state.Enemies.Count);
            for (int i = 0; i < _state.Enemies.Count; i++)
            {
                EnemyStats enemy = _state.Enemies[i];
                if (enemy == null || enemy.IsDead || !enemy.IsDeployed ||
                    !EnemyFormationLayout.IsFullyInsideVisibleRows(enemy) ||
                    !EnemyFormationLayout.IsColumnFront(_state.Enemies, i) ||
                    enemy.justFilledFrontRow || IsIntentRemoved(i))
                {
                    intents.Add(EnemyIntent.None);
                }
                else
                {
                    intents.Add(ApplyActionRange(
                        enemy,
                        _plannedIntents[i]));
                }
            }
            return intents;
        }

        private static EnemyIntent ApplyActionRange(
            EnemyStats enemy,
            EnemyIntent intent)
        {
            bool requiresFrontRow =
                intent.ActionType == EnemyActionType.Attack ||
                intent.Type == EnemyIntentType.Attack ||
                intent.Type == EnemyIntentType.Escape;
            return requiresFrontRow &&
                !EnemyFormationLayout.CanAttackPlayer(enemy)
                ? EnemyIntent.None
                : intent;
        }

        /// <summary>
        /// 为单个敌人生成意图（当前简单策略）。
        /// </summary>
        private EnemyIntent GenerateIntent(EnemyStats enemy, out int choiceIndex)
        {
            choiceIndex = -1;
            if (enemy.behaviorDefinition != null)
                return GenerateConfiguredIntent(enemy, out choiceIndex);

            if (enemy.bossRank == BossRank.Stage)
                return _actionRepeat.SelectStageFallback(enemy, CalculateEnemyDamage(enemy),
                    _state.RandomValue, out choiceIndex);

            float roll = _state.RandomValue();

            // Boss 偏向攻击
            if (enemy.isBoss)
            {
                if (roll < 0.7f)
                    return EnemyIntent.Attack(CalculateEnemyDamage(enemy));
                if (roll < 0.9f)
                    return EnemyIntent.Defend(5);
                return EnemyIntent.None;
            }

            // 普通敌人
            if (roll < 0.6f)
                return EnemyIntent.Attack(CalculateEnemyDamage(enemy));
            if (roll < 0.75f)
                return EnemyIntent.Defend(3);
            return EnemyIntent.None;
        }

        private EnemyIntent GenerateConfiguredIntent(EnemyStats enemy, out int choiceIndex)
        {
            EnemyBehaviorAction action;
            if (enemy.behaviorDefinition.HealthGroups != null)
            {
                EnemyHealthBehaviorPlan plan = GetHealthSelector(enemy).Plan(
                    enemy.currentHealth, enemy.baseHealth, _state.RandomFloatRange,
                    candidate => CanExecuteHealthBehavior(enemy, candidate), _actionRepeat.Capture(enemy));
                // This legacy field is only a read projection in health-group mode.
                enemy.behaviorPhaseIndex = plan.CurrentPhaseIndex;
                if (!plan.HasChoice)
                {
                    choiceIndex = -1;
                    return EnemyIntent.None;
                }
                action = plan.Action;
                choiceIndex = plan.ChoiceIndex;
            }
            else
            {
                enemy.behaviorDefinition.TryEnterPhaseTwo(enemy);
                EnemyBehaviorSelection selection = enemy.behaviorDefinition.Select(
                    enemy.behaviorActionIndex,
                    _state.RandomRange,
                    enemy.behaviorPhaseIndex,
                    _actionRepeat.Capture(enemy));
                action = selection.Action;
                choiceIndex = selection.ChoiceIndex;
            }
            EnemyIntent intent = action.IntentType switch
            {
                EnemyIntentType.Attack => EnemyIntent.Attack(
                    CalculateEnemyDamage(enemy, action.Amount),
                    action.Description,
                    action.ActionId),
                EnemyIntentType.Defend => EnemyIntent.Defend(
                    action.Amount,
                    action.Description,
                    action.ActionId),
                EnemyIntentType.Wait => EnemyIntent.Wait(action.Description, action.ActionId),
                EnemyIntentType.Escape => EnemyIntent.Escape(
                    action.Description,
                    action.ActionId),
                EnemyIntentType.AddEncounterTemporaryCard =>
                    EnemyIntent.AddEncounterTemporaryCard(
                        action.TemporaryCard,
                        action.Description,
                        action.ActionId),
                EnemyIntentType.Compound => BuildCompoundIntent(action),
                _ => EnemyIntent.None
            };
            return intent;
        }

        private bool CanExecuteHealthBehavior(EnemyStats enemy, EnemyBehaviorAction action)
        {
            switch (action.IntentType)
            {
                case EnemyIntentType.Attack:
                case EnemyIntentType.Defend:
                case EnemyIntentType.Wait:
                case EnemyIntentType.Escape:
                    return true;
                case EnemyIntentType.AddEncounterTemporaryCard:
                    return _state.Deck.CanGrantEnemyTemporaryCards(action.TemporaryCard);
                case EnemyIntentType.Compound:
                    return action.Compound.SecondaryIntentType != EnemyIntentType.AddEncounterTemporaryCard ||
                        _state.Deck.CanGrantEnemyTemporaryCards(action.Compound.SecondaryTemporaryCard);
                default:
                    return false;
            }
        }

        private static EnemyIntent BuildCompoundIntent(
            EnemyBehaviorAction action)
        {
            EnemyIntent primary = action.Compound.PrimaryIntentType switch
            {
                EnemyIntentType.Attack => EnemyIntent.Attack(
                    action.Amount,
                    action.Description,
                    action.ActionId),
                EnemyIntentType.Defend => EnemyIntent.Defend(
                    action.Amount,
                    action.Description,
                    action.ActionId),
                EnemyIntentType.Wait => EnemyIntent.Wait(action.Description, action.ActionId),
                _ => throw new InvalidOperationException(
                    $"复合行动 {action.ActionId} 的主行动类型无效：" +
                    action.Compound.PrimaryIntentType)
            };
            EnemyIntent secondary = action.Compound.SecondaryIntentType switch
            {
                EnemyIntentType.AddEncounterTemporaryCard =>
                    EnemyIntent.AddEncounterTemporaryCard(
                        action.Compound.SecondaryTemporaryCard,
                        action.Compound.SecondaryDescription,
                        action.Compound.SecondaryActionId),
                EnemyIntentType.Attack => EnemyIntent.Attack(
                    action.Compound.SecondaryAmount,
                    action.Compound.SecondaryDescription,
                    action.Compound.SecondaryActionId),
                EnemyIntentType.Defend => EnemyIntent.Defend(
                    action.Compound.SecondaryAmount,
                    action.Compound.SecondaryDescription,
                    action.Compound.SecondaryActionId),
                EnemyIntentType.Wait => EnemyIntent.Wait(
                    action.Compound.SecondaryDescription,
                    action.Compound.SecondaryActionId),
                _ => throw new InvalidOperationException(
                    $"复合行动 {action.ActionId} 的次行动类型无效：" +
                    action.Compound.SecondaryIntentType)
            };
            return EnemyIntent.Compound(
                primary,
                secondary,
                action.Description,
                action.ActionId);
        }

        /// <summary>
        /// 计算敌人攻击伤害。
        /// 基础伤害 = maxHit（敌人每次最大承受伤害值的反向用作攻击力）。
        /// 可被招煞（Curse）属性加成。
        /// </summary>
        private int CalculateEnemyDamage(EnemyStats enemy)
            => CalculateEnemyDamage(enemy, enemy.maxHit);

        private int CalculateEnemyDamage(
            EnemyStats enemy,
            int configuredBaseDamage)
        {
            int baseDamage = Math.Max(1, configuredBaseDamage);
            // 招煞加成
            float curseMult = PlayerStats.GetMultiplier(_state.Player.zhaoSha);
            return Mathf.FloorToInt(baseDamage * curseMult + 0.5f);
        }

        /// <summary>Validates an issued pending batch entry without selecting or mutating it.</summary>
        public bool TryValidateReceipt(EnemyActionCommand command)
        {
            int index = command.EnemyIndex;
            if (index < 0 || index >= _state.Enemies.Count || command.Intent.IsNone)
                return false;
            EnemyStats enemy = _state.Enemies[index];
            if (enemy == null || enemy.IsDead || enemy.IsWithdrawn || !enemy.IsDeployed ||
                IsIntentRemoved(index))
                return false;
            // The queue's read-only peek uses the same physical admission as
            // execution, so presentation cannot target an actor that will be skipped.
            if (!EnemyFormationLayout.IsFullyInsideVisibleRows(enemy)) return false;
            bool isEscape = command.Intent.Type == EnemyIntentType.Escape;
            bool requiresFrontRow = command.Intent.ActionType == EnemyActionType.Attack ||
                command.Intent.Type == EnemyIntentType.Attack || isEscape;
            if (requiresFrontRow && !EnemyFormationLayout.CanAttackPlayer(enemy)) return false;
            if (isEscape && enemy.justFilledFrontRow) return false;
            EnemyActionReceipt receipt = command.Receipt;
            if (receipt == null)
                return true; // Legacy commands keep their existing execution contract.
            if (_claimedReceipts.Contains(receipt) ||
                (!_outstandingReceipts.Contains(receipt) &&
                 (!_previewReceipts.TryGetValue(enemy.runtimeId, out EnemyActionReceipt preview) ||
                  !ReferenceEquals(preview, receipt))))
                return false;
            return receipt.EnemyIndex == index && receipt.EnemyRuntimeId == enemy.runtimeId &&
                   ReferenceEquals(receipt.Actor, enemy) &&
                   ReferenceEquals(receipt.Definition, enemy.behaviorDefinition) &&
                   MatchesPlannedIntent(receipt.Intent, command.Intent);
        }

        /// <summary>
        /// Claims before synchronous effects publish events. A failed effect cannot
        /// retry this entry; claiming itself never advances behavior or action count.
        /// </summary>
        public bool TryBeginAction(EnemyActionCommand command)
        {
            if (!TryValidateReceipt(command)) return false;
            EnemyActionReceipt receipt = command.Receipt;
            if (receipt == null) return true;
            _outstandingReceipts.Remove(receipt);
            if (_previewReceipts.TryGetValue(receipt.EnemyRuntimeId, out EnemyActionReceipt preview) &&
                ReferenceEquals(preview, receipt))
                _previewReceipts.Remove(receipt.EnemyRuntimeId);
            _claimedReceipts.Add(receipt);
            return true;
        }

        /// <summary>
        /// Completes a successful claim once. Direct rule callers may atomically
        /// claim and complete; later previews/phases do not replace its frozen plan.
        /// </summary>
        public bool MarkActionResolved(EnemyActionCommand command)
        {
            EnemyActionReceipt receipt = command.Receipt;
            if (receipt == null)
            {
                MarkActionResolved(command.EnemyIndex, command.Intent);
                return true;
            }
            if (command.EnemyIndex != receipt.EnemyIndex ||
                !MatchesPlannedIntent(receipt.Intent, command.Intent) ||
                receipt.EnemyIndex < 0 || receipt.EnemyIndex >= _state.Enemies.Count ||
                !ReferenceEquals(_state.Enemies[receipt.EnemyIndex], receipt.Actor) ||
                receipt.Actor.runtimeId != receipt.EnemyRuntimeId ||
                !ReferenceEquals(receipt.Actor.behaviorDefinition, receipt.Definition))
                return false;
            if (!_claimedReceipts.Contains(receipt) && !TryBeginAction(command)) return false;
            // Validate the immutable payload before consuming: a forged completion
            // must not steal the legitimate in-flight claim. Consume before counters.
            _claimedReceipts.Remove(receipt);
            EnemyStats enemy = receipt.Actor;
            EnemyBehaviorDefinition definition = receipt.Definition;
            _actionRepeat.Commit(receipt);
            if (definition == null) return true;
            if (definition.HealthGroups == null &&
                definition.SelectionMode == EnemyBehaviorSelectionMode.FixedSequence)
            {
                if (enemy.bossRank != BossRank.Stage || enemy.behaviorPhaseIndex == receipt.PhaseIndex)
                    enemy.behaviorActionIndex = definition.Advance(
                        enemy.bossRank == BossRank.Stage ? receipt.ChoiceIndex : enemy.behaviorActionIndex,
                        receipt.PhaseIndex);
            }
            enemy.behaviorLegalActionCount = enemy.behaviorLegalActionCount == int.MaxValue
                ? int.MaxValue : enemy.behaviorLegalActionCount + 1;
            return true;
        }

        /// <summary>Encounter terminal cleanup; failed claims remain non-retryable until this boundary.</summary>
        internal void ClearActionReceipts()
        {
            _previewReceipts.Clear();
            _outstandingReceipts.Clear();
            _claimedReceipts.Clear();
            _plannedIntents = null;
        }

        /// <summary>Compatibility completion for callers that only carry the current preview's intent.</summary>
        public void MarkActionResolved(
            int enemyIndex,
            EnemyIntent resolvedIntent)
        {
            if (enemyIndex < 0 || enemyIndex >= _state.Enemies.Count ||
                IsIntentRemoved(enemyIndex))
                return;

            EnemyStats enemy = _state.Enemies[enemyIndex];
            EnemyBehaviorDefinition definition = enemy?.behaviorDefinition;
            if (enemy == null || resolvedIntent.IsNone)
                return;
            if (_previewReceipts.TryGetValue(enemy.runtimeId, out EnemyActionReceipt preview))
            {
                if (MatchesPlannedIntent(preview.Intent, resolvedIntent))
                    MarkActionResolved(new EnemyActionCommand(preview));
                return;
            }
            if (definition == null || enemy.bossRank == BossRank.Stage)
                return; // Stage history requires an issued, successfully consumed receipt.
            if (definition.HealthGroups != null ||
                definition.SelectionMode == EnemyBehaviorSelectionMode.WeightedRandom)
                return;

            EnemyBehaviorAction expected = definition.GetAction(
                enemy.behaviorActionIndex,
                enemy.behaviorPhaseIndex);
            if (expected.IntentType != resolvedIntent.Type)
                return;

            enemy.behaviorActionIndex = definition.Advance(
                enemy.behaviorActionIndex,
                enemy.behaviorPhaseIndex);
            enemy.behaviorLegalActionCount =
                enemy.behaviorLegalActionCount == int.MaxValue
                    ? int.MaxValue
                    : enemy.behaviorLegalActionCount + 1;
        }

        private static bool MatchesPlannedIntent(
            EnemyIntent planned,
            EnemyIntent resolved)
        {
            return planned.Type == resolved.Type &&
                   planned.Amount == resolved.Amount &&
                   planned.TargetCount == resolved.TargetCount &&
                   planned.ActionType == resolved.ActionType &&
                   planned.CanBeDisarmed == resolved.CanBeDisarmed &&
                   string.Equals(
                       planned.ActionId,
                       resolved.ActionId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       planned.Description,
                       resolved.Description,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       planned.TemporaryCard.ContentId,
                       resolved.TemporaryCard.ContentId,
                       StringComparison.Ordinal) &&
                   planned.TemporaryCard.Count ==
                       resolved.TemporaryCard.Count &&
                   planned.TemporaryCard.Placement ==
                       resolved.TemporaryCard.Placement &&
                   planned.TemporaryCard.MaxRemaining ==
                       resolved.TemporaryCard.MaxRemaining &&
                   ReferenceEquals(planned.TemporaryCard.Definition, resolved.TemporaryCard.Definition) &&
                   string.Equals(
                       planned.TemporaryCard.SourceBehaviorContentId,
                       resolved.TemporaryCard.SourceBehaviorContentId,
                       StringComparison.Ordinal) &&
                   planned.SecondaryType == resolved.SecondaryType &&
                   planned.PrimaryType == resolved.PrimaryType &&
                   planned.SecondaryAmount == resolved.SecondaryAmount &&
                   string.Equals(
                       planned.SecondaryDescription,
                       resolved.SecondaryDescription,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       planned.SecondaryActionId,
                       resolved.SecondaryActionId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       planned.SecondaryTemporaryCard.ContentId,
                       resolved.SecondaryTemporaryCard.ContentId,
                       StringComparison.Ordinal) &&
                   planned.SecondaryTemporaryCard.Count ==
                       resolved.SecondaryTemporaryCard.Count &&
                   planned.SecondaryTemporaryCard.Placement ==
                       resolved.SecondaryTemporaryCard.Placement &&
                   planned.SecondaryTemporaryCard.MaxRemaining ==
                       resolved.SecondaryTemporaryCard.MaxRemaining &&
                   ReferenceEquals(planned.SecondaryTemporaryCard.Definition, resolved.SecondaryTemporaryCard.Definition) &&
                   string.Equals(
                       planned.SecondaryTemporaryCard.SourceBehaviorContentId,
                       resolved.SecondaryTemporaryCard.SourceBehaviorContentId,
                       StringComparison.Ordinal);
        }

        /// <summary>
        /// 生成所有存活敌人的行动指令。
        /// </summary>
        public List<EnemyActionCommand> GenerateCommands()
        {
            var commands = new List<EnemyActionCommand>();
            if (_state.EnemyInterrupts?.HasFrontInterrupts == true) return commands;
            var intents = GenerateIntents();
            for (int i = 0; i < intents.Count; i++)
            {
                EnemyStats enemy = _state.Enemies[i];
                if (!intents[i].IsNone && enemy != null &&
                    _previewReceipts.TryGetValue(enemy.runtimeId, out EnemyActionReceipt receipt))
                {
                    var command = new EnemyActionCommand(receipt);
                    if (!TryValidateReceipt(command)) continue;
                    _outstandingReceipts.Add(receipt);
                    commands.Add(command);
                }
            }
            return commands;
        }

        /// <summary>
        /// Each zero captures a distinct descending-column front-row batch from the
        /// existing preview. Ordinary actors participate too; this never applies
        /// the normal global gate or selects future actions. Its receipts cannot
        /// be coalesced with an earlier normal/interrupt batch of the same intent.
        /// </summary>
        public List<EnemyActionCommand> CaptureInterruptCommands()
        {
            var commands = new List<EnemyActionCommand>();
            IReadOnlyList<EnemyIntent> intents = GenerateIntents();
            var frontIndices = new List<int>();
            var capturedActors = new HashSet<EnemyStats>();
            for (int i = 0; i < intents.Count; i++)
            {
                EnemyStats enemy = _state.Enemies[i];
                if (enemy == null || intents[i].IsNone || !EnemyFormationLayout.CanAttackPlayer(enemy) ||
                    !_previewReceipts.TryGetValue(enemy.runtimeId, out EnemyActionReceipt preview) ||
                    !TryValidateReceipt(new EnemyActionCommand(preview)))
                    continue;
                if (capturedActors.Add(enemy)) frontIndices.Add(i);
            }
            // Source FrontEnemies are fixed column slots. Roster order is an
            // independent project identity order and must not determine this batch.
            frontIndices.Sort((left, right) =>
            {
                int columns = _state.Enemies[right].column.CompareTo(_state.Enemies[left].column);
                return columns != 0 ? columns : left.CompareTo(right);
            });
            foreach (int i in frontIndices)
            {
                EnemyStats enemy = _state.Enemies[i];
                EnemyActionReceipt preview = _previewReceipts[enemy.runtimeId];
                var receipt = new EnemyActionReceipt(i, enemy, preview.Definition,
                    preview.ChoiceIndex, preview.PhaseIndex, preview.ActionIndex, intents[i]);
                _outstandingReceipts.Add(receipt);
                commands.Add(new EnemyActionCommand(receipt));
            }
            return commands;
        }
    }
}
