using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>卡牌归元后写入 Run 的永久修正类型。</summary>
    public enum RunCardConversionOutcome
    {
        None = 0,
        BaseArmorPerTurn = 1,
        Might = 2,
        BaseMana = 3,
        Area = 4,
        Luck = 5
    }

    public enum RunCardConversionSelectionPolicy
    {
        MappedCards = 0,
        AnyPersistentNonHero = 1
    }

    public enum RunCardConversionCashOutPolicy
    {
        Allowed = 0,
        Forbidden = 1
    }

    public enum RunCardConversionExitPolicy
    {
        AllowLeaveBeforeCommit = 0,
        RequireDisposition = 1
    }

    [Serializable]
    public sealed class RunCardConversionRule
    {
        [Tooltip("允许归元的卡牌稳定 contentId")]
        public string sourceCardContentId;
        public RunCardConversionOutcome outcome;
        [Min(1)] public int amount = 1;

        /// <summary>
        /// Offer and commit share the same effective gain, including the
        /// existing Run luck safety cap. Saturated conversions cost no card.
        /// </summary>
        public int GetEffectiveAmount(int currentValue)
        {
            int cap = outcome == RunCardConversionOutcome.Luck
                ? RunProgressionState.DefaultLuckSafetyCap
                : int.MaxValue;
            return (int)Math.Max(0L,
                Math.Min(amount, (long)cap - currentValue));
        }
    }

    /// <summary>
    /// Owns which persistent card instances a conversion accepts, the resulting
    /// Run modifier, and permitted alternatives. It does not own the Run deck
    /// or presentation. Zero-valued policies preserve existing mapped shrines.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewRunCardConversion",
        menuName = "万界长卷/Run 卡牌归元规则")]
    public sealed class RunCardConversionRuleProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        [Tooltip("稳定规则 contentId")]
        public string contentId;
        public RunCardConversionSelectionPolicy selectionPolicy;
        public RunCardConversionCashOutPolicy cashOutPolicy;
        public RunCardConversionExitPolicy exitPolicy;
        [Tooltip("任意持久非英灵牌模式的固定修正；映射模式仍使用 rules")]
        public RunCardConversionOutcome fixedOutcome;
        [Min(1)] public int fixedAmount = 1;
        public List<RunCardConversionRule> rules =
            new List<RunCardConversionRule>();

        public bool AllowsCashOut => cashOutPolicy == RunCardConversionCashOutPolicy.Allowed;
        public bool AllowsLeave => exitPolicy == RunCardConversionExitPolicy.AllowLeaveBeforeCommit;
        /// <summary>
        /// Identifies the generic Mana exchange. The historical property name
        /// remains for consumer compatibility; new exchanges grant Mana directly.
        /// Separate pickups exist only when recovering old schema-41 saves.
        /// </summary>
        public bool RequiresManaPickup => selectionPolicy == RunCardConversionSelectionPolicy.AnyPersistentNonHero &&
            fixedOutcome == RunCardConversionOutcome.BaseMana;

        /// <summary>
        /// Resolves eligibility and reward for a real, persistent non-hero card.
        /// The transaction separately proves that the exact instance still
        /// belongs to this Run; passing a display clone does not confer ownership.
        /// </summary>
        public bool TryResolveRule(CardConfig card, out RunCardConversionRule rule)
        {
            rule = null;
            if (card == null || card.runtimeInstanceId <= 0 ||
                string.IsNullOrWhiteSpace(card.contentId) ||
                card.IsEncounterTemporary || IsHeroCard(card))
                return false;
            if (selectionPolicy == RunCardConversionSelectionPolicy.MappedCards)
                return TryGetRule(card.contentId, out rule);
            if (selectionPolicy != RunCardConversionSelectionPolicy.AnyPersistentNonHero ||
                !IsValidOutcome(fixedOutcome) || fixedAmount <= 0)
                return false;
            rule = new RunCardConversionRule
            {
                sourceCardContentId = card.contentId,
                outcome = fixedOutcome,
                amount = fixedAmount
            };
            return true;
        }

        /// <summary>Legacy author-mapping lookup; instance choices must use TryResolveRule.</summary>
        public bool TryGetRule(
            string sourceCardContentId,
            out RunCardConversionRule rule)
        {
            rule = null;
            if (selectionPolicy != RunCardConversionSelectionPolicy.MappedCards ||
                string.IsNullOrWhiteSpace(sourceCardContentId) || rules == null)
                return false;
            for (int i = 0; i < rules.Count; i++)
            {
                RunCardConversionRule candidate = rules[i];
                if (candidate != null && string.Equals(
                        candidate.sourceCardContentId,
                        sourceCardContentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    rule = candidate;
                    return true;
                }
            }
            return false;
        }

        public List<string> ValidateProfile(
            IReadOnlyList<CardConfig> cardPool)
        {
            var errors = new List<string>();
            if (schemaVersion != CurrentSchemaVersion)
                errors.Add($"schemaVersion 必须为 {CurrentSchemaVersion}。");
            if (string.IsNullOrWhiteSpace(contentId))
                errors.Add("归元规则 contentId 不能为空。");
            if (!Enum.IsDefined(typeof(RunCardConversionSelectionPolicy), selectionPolicy))
                errors.Add("归元选牌策略无效。");
            if (!Enum.IsDefined(typeof(RunCardConversionCashOutPolicy), cashOutPolicy))
                errors.Add("归元化玉策略无效。");
            if (!Enum.IsDefined(typeof(RunCardConversionExitPolicy), exitPolicy))
                errors.Add("归元退出策略无效。");
            if (selectionPolicy == RunCardConversionSelectionPolicy.AnyPersistentNonHero)
            {
                if (!IsValidOutcome(fixedOutcome))
                    errors.Add("任意持久牌交换的固定结果无效。");
                if (fixedAmount <= 0)
                    errors.Add("任意持久牌交换的固定数值必须大于 0。");
                if (rules != null && rules.Count > 0)
                    errors.Add("任意持久牌交换不能同时配置卡牌映射。");
                return errors;
            }
            if (rules == null || rules.Count == 0)
            {
                errors.Add("归元规则至少需要一条卡牌映射。");
                return errors;
            }

            var cardsById = new Dictionary<string, CardConfig>(
                StringComparer.OrdinalIgnoreCase);
            if (cardPool != null)
            {
                for (int i = 0; i < cardPool.Count; i++)
                {
                    CardConfig card = cardPool[i];
                    if (card != null &&
                        !string.IsNullOrWhiteSpace(card.contentId) &&
                        !cardsById.ContainsKey(card.contentId))
                    {
                        cardsById.Add(card.contentId, card);
                    }
                }
            }

            var sourceIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rules.Count; i++)
            {
                RunCardConversionRule rule = rules[i];
                if (rule == null)
                {
                    errors.Add($"rules[{i}] 不能为空。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(rule.sourceCardContentId))
                {
                    errors.Add($"rules[{i}] 缺少 sourceCardContentId。");
                    continue;
                }
                if (!sourceIds.Add(rule.sourceCardContentId))
                    errors.Add($"归元卡牌 ID 重复：{rule.sourceCardContentId}。");
                if (!IsValidOutcome(rule.outcome))
                {
                    errors.Add(
                        $"卡牌 {rule.sourceCardContentId} 的归元结果无效。");
                }
                if (rule.amount <= 0)
                    errors.Add(
                        $"卡牌 {rule.sourceCardContentId} 的归元数值必须大于 0。");
                if (!cardsById.TryGetValue(
                        rule.sourceCardContentId, out CardConfig source))
                {
                    errors.Add($"归元规则引用不存在的卡牌：" +
                               $"{rule.sourceCardContentId}。");
                }
                else if (IsHeroCard(source))
                {
                    errors.Add($"英灵卡不能进入归元规则：" +
                               $"{rule.sourceCardContentId}。");
                }
            }
            return errors;
        }

        private static bool IsValidOutcome(RunCardConversionOutcome outcome)
            => outcome != RunCardConversionOutcome.None &&
                Enum.IsDefined(typeof(RunCardConversionOutcome), outcome);

        public static bool IsHeroCard(CardConfig card)
        {
            if (card?.effects == null)
                return false;
            for (int i = 0; i < card.effects.Count; i++)
            {
                CardEffectSpec effect = card.effects[i];
                if (effect != null &&
                    effect.effectType == EffectType.SummonCrawler)
                    return true;
            }
            return false;
        }
    }

    public static class RunCardConversionRuleCatalog
    {
        private const string ResourceDirectory =
            "Content/RunCardConversions";
        private static readonly Dictionary<string, RunCardConversionRuleProfile>
            Cache = new Dictionary<string, RunCardConversionRuleProfile>(
                StringComparer.OrdinalIgnoreCase);

        public static RunCardConversionRuleProfile Get(string contentId)
        {
            if (!TryGet(contentId, out RunCardConversionRuleProfile profile))
            {
                throw new InvalidOperationException(
                    $"找不到 Run 卡牌归元规则：{contentId}。");
            }
            return profile;
        }

        public static bool TryGet(
            string contentId,
            out RunCardConversionRuleProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(contentId))
                return false;
            EnsureLoaded();
            return Cache.TryGetValue(contentId, out profile);
        }

        public static void ClearCache()
        {
            Cache.Clear();
        }

        private static void EnsureLoaded()
        {
            if (Cache.Count > 0)
                return;
            RunCardConversionRuleProfile[] profiles =
                Resources.LoadAll<RunCardConversionRuleProfile>(
                    ResourceDirectory);
            for (int i = 0; i < profiles.Length; i++)
            {
                RunCardConversionRuleProfile profile = profiles[i];
                if (profile == null ||
                    string.IsNullOrWhiteSpace(profile.contentId))
                    continue;
                if (Cache.ContainsKey(profile.contentId))
                {
                    throw new InvalidOperationException(
                        $"RunCardConversionRuleProfile contentId 重复：" +
                        $"{profile.contentId}。");
                }
                Cache.Add(profile.contentId, profile);
            }
        }
    }
}
