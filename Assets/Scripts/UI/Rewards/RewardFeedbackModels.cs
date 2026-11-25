using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.UI.Rewards
{
    public enum RewardFeedbackKind
    {
        Card,
        BaseMana,
        Jade,
        Relic,
        Seal,
        Stat,
        Generic,
        Unavailable,
        CurrentHealth
    }

    public enum RewardUtilityActionKind
    {
        Refresh,
        Abandon,
        Exclude,
        CashOut
    }

    public readonly struct RewardUtilityAvailability
    {
        public static RewardUtilityAvailability Locked => default;

        public bool CanRefresh { get; }
        public bool CanAbandon { get; }
        public bool CanExclude { get; }

        public RewardUtilityAvailability(
            bool canRefresh,
            bool canAbandon,
            bool canExclude)
        {
            CanRefresh = canRefresh;
            CanAbandon = canAbandon;
            CanExclude = canExclude;
        }
    }

    /// <summary>
    /// Owns the mapping from typed reward facts to display identity and text.
    /// This file exceeds the 500-line review threshold because it lists reward
    /// variants; it owns no transaction, gameplay calculation or lifecycle.
    /// Numeric result text comes from receipts, never stale authored prose.
    /// </summary>
    [Serializable]
    public sealed class RewardFeedbackItem
    {
        public string ReceiptId;
        public RewardFeedbackKind Kind;
        public string Title;
        public string DisplayName;
        public string Description;
        /// <summary>Short committed before/after result visible without hovering.</summary>
        public string ProgressText;
        /// <summary>Optional visible guidance for a pending reward decision; never an action or rule.</summary>
        public string DecisionHint;
        public string PresentationId;
        public string Symbol;
        public int Quantity;
        public int ValueBefore;
        public int ValueAfter;
        public Sprite Artwork;
        public CardConfig Card;
        public RewardRarity Rarity = RewardRarity.Common;
        public Color Accent = new Color(0.73f, 0.56f, 0.31f, 1f);

        /// <summary>Currency identity is shared by pickups, cash-outs and offers.
        /// Callers may supply context text, but never need to bind its artwork.</summary>
        public static RewardFeedbackItem FromJade(string receiptId, int quantity,
            string description = null)
        {
            return new RewardFeedbackItem
            {
                ReceiptId = receiptId,
                Kind = RewardFeedbackKind.Jade,
                Title = $"获得灵玉 +{quantity}",
                DisplayName = "灵玉",
                Description = $"灵玉 +{quantity}。\n" +
                    (description ?? "灵玉可用于局外成长。"),
                PresentationId = RewardArtworkCatalog.JadePresentationId,
                Artwork = RewardArtworkCatalog.Resolve(RewardArtworkCatalog.JadePresentationId),
                Quantity = quantity,
                Accent = new Color(0.55f, 0.72f, 0.52f, 1f)
            };
        }

        public static RewardFeedbackItem FromCard(
            string receiptId,
            CardConfig card)
        {
            string description = card != null
                ? Cards.CardTextRenderer.GetDisplayDescription(CardDisplayData.FromCard(card))
                : string.Empty;
            return new RewardFeedbackItem
            {
                ReceiptId = receiptId,
                Kind = RewardFeedbackKind.Card,
                Title = "获得卡牌",
                DisplayName = card != null ? card.cardName : "未知卡牌",
                Description = description,
                Card = card,
                Artwork = card != null ? card.cardArt : null,
                Rarity = RewardRarity.Common
            };
        }

        /// <summary>
        /// 把已确定但尚未提交宿主的灵印转换为统一奖励主体。获得态、后续多候选
        /// 奖励和宿主分配因此共用同一份稳定展示身份，不再按显示名或固定图片拼装。
        /// </summary>
        public static RewardFeedbackItem FromSeal(LingyinConfig seal)
        {
            if (seal == null)
                throw new ArgumentNullException(nameof(seal));

            return new RewardFeedbackItem
            {
                ReceiptId = seal.contentId,
                Kind = RewardFeedbackKind.Seal,
                Title = string.IsNullOrWhiteSpace(seal.displayName)
                    ? "获得灵印"
                    : $"获得灵印 · {seal.displayName}",
                DisplayName = seal.displayName,
                Description = seal.effectType switch
                {
                    CardSealEffect.Draw => seal.effectValue == 1
                        ? "抽1张牌" : $"抽{seal.effectValue}张牌",
                    CardSealEffect.Armor => $"护甲+{seal.effectValue}",
                    CardSealEffect.Amount => $"数量+{seal.effectValue}",
                    CardSealEffect.BengLie => $"裂空+{seal.effectValue}%",
                    _ => seal.description
                },
                PresentationId = seal.presentationId,
                Symbol = seal.sealCharacter,
                Artwork = RewardArtworkCatalog.Resolve(seal.presentationId),
                Rarity = ResolveSealRarity(seal.grade),
                Accent = new Color(0.58f, 0.48f, 0.72f, 1f)
            };
        }

        /// <summary>
        /// 把类型化地图结算回执转换为通用反馈模型；地图协调器不需要了解
        /// MP、灵玉、遗物或灵印各自的文案与配色。
        /// </summary>
        public static RewardFeedbackItem FromInteractionReceipt(
            LevelInteractionReceipt receipt,
            int? configuredHealingAmount = null)
        {
            // Modifier receipts carry requested Amount but authoritative before/
            // after bonuses; the delta also remains accurate at saturation.
            int appliedIncrease = Math.Max(0, receipt.ValueAfter - receipt.ValueBefore);
            switch (receipt.Kind)
            {
                case LevelInteractionEffectKind.ManaStatue:
                case LevelInteractionEffectKind.ManaOffering:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.BaseMana,
                        Title = $"拾取灵力 {receipt.Amount} 点",
                        DisplayName = "灵力补给",
                        Description = $"拾取 {receipt.Amount} 点灵力。\n" +
                            $"当前灵力：{receipt.ValueBefore} → {receipt.ValueAfter}。\n" +
                            "本次探索后续战斗的每回合基础灵力加成已同步结算。",
                        PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                            receipt.Kind, receipt.PresentationId),
                        Artwork = RewardArtworkCatalog.ResolveInteractionBuff(LevelInteractionEffectKind.AddBaseMana),
                        Quantity = receipt.Amount,
                        ValueBefore = receipt.ValueBefore,
                        ValueAfter = receipt.ValueAfter,
                        Rarity = receipt.Rarity,
                        Accent = new Color(0.40f, 0.72f, 0.78f, 1f)
                    };
                case LevelInteractionEffectKind.AddBaseMana:
                    return FromBaseManaGain(receipt);
                case LevelInteractionEffectKind.AddHandLimit:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.Stat,
                        Title = $"每回合抽牌 +{appliedIncrease}",
                        DisplayName = "抽牌赐福",
                        Description = $"本次探索后续战斗中，每回合自动抽牌增加 {appliedIncrease} 张；不立即抽牌。\n" +
                            $"本次探索抽牌加成：{receipt.ValueBefore} → {receipt.ValueAfter}。",
                        PresentationId = string.IsNullOrWhiteSpace(
                                receipt.PresentationId)
                            ? RewardArtworkCatalog.HandPresentationId
                            : receipt.PresentationId,
                        Artwork = RewardArtworkCatalog.Resolve(
                            string.IsNullOrWhiteSpace(receipt.PresentationId)
                                ? RewardArtworkCatalog.HandPresentationId
                                : receipt.PresentationId),
                        Quantity = appliedIncrease,
                        ValueBefore = receipt.ValueBefore,
                        ValueAfter = receipt.ValueAfter,
                        Rarity = receipt.Rarity,
                        Accent = new Color(0.45f, 0.73f, 0.66f, 1f)
                    };
                case LevelInteractionEffectKind.AddBaseArmor:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.Stat,
                        Title = $"基础护甲 +{appliedIncrease}",
                        DisplayName = "护甲赐福",
                        Description = $"本次探索后续战斗中，每回合基础护甲 +{appliedIncrease}。\n" +
                            $"本次探索基础护甲加成：{receipt.ValueBefore} → {receipt.ValueAfter}。",
                        PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                            receipt.Kind, receipt.PresentationId),
                        Artwork = RewardArtworkCatalog.ResolveInteractionBuff(receipt.Kind),
                        Quantity = appliedIncrease,
                        ValueBefore = receipt.ValueBefore,
                        ValueAfter = receipt.ValueAfter,
                        Rarity = receipt.Rarity,
                        Accent = new Color(0.50f, 0.68f, 0.78f, 1f)
                    };
                case LevelInteractionEffectKind.GrantJade:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.Jade,
                        Title = $"获得灵玉 +{receipt.Amount}",
                        DisplayName = "灵玉",
                        Description = $"实际获得灵玉 {receipt.Amount}。\n" +
                            $"灵玉余额：{receipt.ValueBefore} → {receipt.ValueAfter}。\n灵玉可用于局外成长。",
                        PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                            receipt.Kind, receipt.PresentationId),
                        Artwork = RewardArtworkCatalog.ResolveInteractionBuff(
                            receipt.Kind),
                        Quantity = receipt.Amount,
                        ValueBefore = receipt.ValueBefore,
                        ValueAfter = receipt.ValueAfter,
                        Rarity = receipt.Rarity,
                        Accent = new Color(0.55f, 0.72f, 0.52f, 1f)
                    };
                case LevelInteractionEffectKind.GrantRelic:
                    {
                        string relicName = SummaryOr(receipt, "未知遗珍");
                        string presentationId = receipt.PresentationId;
                        if (string.IsNullOrWhiteSpace(presentationId))
                            presentationId = RewardArtworkCatalog.ResolveRelicPresentationId(
                                receipt.TargetContentId);
                        return new RewardFeedbackItem
                        {
                            ReceiptId = receipt.EffectId,
                            Kind = RewardFeedbackKind.Relic,
                            Title = $"获得遗珍 · {relicName}",
                            DisplayName = relicName,
                            Description = DetailOr(
                                receipt, receipt.TargetContentId),
                            PresentationId = presentationId,
                            Artwork = RewardArtworkCatalog.Resolve(presentationId),
                            Quantity = receipt.Amount,
                            Rarity = receipt.Rarity,
                            Accent = new Color(0.72f, 0.57f, 0.34f, 1f)
                        };
                    }
                case LevelInteractionEffectKind.GrantSeal:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.Seal,
                        Title = "获得灵印",
                        DisplayName = SummaryOr(receipt, "获得灵印"),
                        Description = DetailOr(
                            receipt, receipt.TargetContentId),
                        PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                            receipt.Kind, receipt.PresentationId),
                        Artwork = RewardArtworkCatalog.Resolve(
                            receipt.PresentationId),
                        Quantity = receipt.Amount,
                        Rarity = receipt.Rarity,
                        Accent = new Color(0.58f, 0.48f, 0.72f, 1f)
                    };
                case LevelInteractionEffectKind.HealCurrentHealth:
                case LevelInteractionEffectKind.ChickenOffering:
                    bool recoveredHealth = receipt.Amount > 0;
                    int healingCapacity = receipt.Kind == LevelInteractionEffectKind.HealCurrentHealth
                        ? Math.Max(0, configuredHealingAmount ?? receipt.Amount)
                        : 0;
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.CurrentHealth,
                        Title = healingCapacity > 0
                            ? $"生命补给 · 可恢复{healingCapacity}点"
                            : recoveredHealth
                            ? $"恢复生命 +{receipt.Amount}"
                            : "获得生命补给",
                        DisplayName = "生命补给",
                        Description = healingCapacity > 0
                            ? $"恢复当前生命 {healingCapacity} 点，不提高生命上限。\n生命：{receipt.ValueBefore} → {receipt.ValueAfter}。"
                            : $"恢复当前生命 {receipt.Amount} 点（{receipt.ValueBefore} → {receipt.ValueAfter}）。\n不提高生命上限。",
                        PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                            receipt.Kind, receipt.PresentationId),
                        Artwork = null,
                        Quantity = receipt.Amount,
                        ValueBefore = receipt.ValueBefore,
                        ValueAfter = receipt.ValueAfter,
                        Rarity = receipt.Rarity,
                        Accent = new Color(0.82f, 0.19f, 0.16f, 1f)
                    };
                case LevelInteractionEffectKind.ConvertCard:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = receipt.TargetRuntimeInstanceId > 0
                            ? RewardFeedbackKind.Stat
                            : RewardFeedbackKind.Jade,
                        Title = receipt.TargetRuntimeInstanceId > 0
                            ? "卡牌归元"
                            : $"化为灵玉 · +{receipt.Amount}",
                        DisplayName = SummaryOr(receipt, "归元所得"),
                        Description = DetailOr(
                            receipt,
                            receipt.TargetRuntimeInstanceId > 0
                                ? $"永久修正 {receipt.ValueBefore} → " +
                                  $"{receipt.ValueAfter}"
                                : "灵玉已经收入行囊。"),
                        PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                            receipt.Kind, receipt.PresentationId),
                        Artwork = receipt.TargetRuntimeInstanceId > 0
                            ? null
                            : RewardArtworkCatalog.Resolve(
                                RewardArtworkCatalog.JadePresentationId),
                        Quantity = receipt.Amount,
                        ValueBefore = receipt.ValueBefore,
                        ValueAfter = receipt.ValueAfter,
                        Rarity = receipt.Rarity,
                        Accent = receipt.TargetRuntimeInstanceId > 0
                            ? new Color(0.66f, 0.52f, 0.30f, 1f)
                            : new Color(0.55f, 0.72f, 0.52f, 1f)
                    };
                case LevelInteractionEffectKind.AddMight:
                    {
                        string presentationId = string.IsNullOrWhiteSpace(
                            receipt.PresentationId)
                            ? RewardArtworkCatalog.MightPresentationId
                            : receipt.PresentationId;
                        return new RewardFeedbackItem
                        {
                            ReceiptId = receipt.EffectId,
                            Kind = RewardFeedbackKind.Stat,
                            Title = $"锋锐 +{appliedIncrease}%",
                            DisplayName = "锋锐赐福",
                            Description = $"本次探索后续战斗中，锋锐增加 {appliedIncrease} 个百分点。\n" +
                                $"本次探索锋锐加成：{receipt.ValueBefore}% → {receipt.ValueAfter}%。",
                            PresentationId = presentationId,
                            Artwork = RewardArtworkCatalog.Resolve(presentationId),
                            Quantity = appliedIncrease,
                            ValueBefore = receipt.ValueBefore,
                            ValueAfter = receipt.ValueAfter,
                            Rarity = receipt.Rarity,
                            Accent = new Color(0.75f, 0.36f, 0.28f, 1f)
                        };
                    }
                case LevelInteractionEffectKind.AddArea:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.Stat,
                        Title = $"裂空 +{appliedIncrease}%",
                        DisplayName = "裂空赐福",
                        Description = $"本次探索后续战斗中，裂空增加 {appliedIncrease} 个百分点。\n" +
                            $"本次探索裂空加成：{receipt.ValueBefore}% → {receipt.ValueAfter}%。",
                        PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                            receipt.Kind, receipt.PresentationId),
                        Artwork = RewardArtworkCatalog.ResolveInteractionBuff(
                            receipt.Kind),
                        Quantity = appliedIncrease,
                        ValueBefore = receipt.ValueBefore,
                        ValueAfter = receipt.ValueAfter,
                        Rarity = receipt.Rarity,
                        Accent = new Color(0.34f, 0.66f, 0.78f, 1f)
                    };
                case LevelInteractionEffectKind.AddRewardReroll:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.Stat,
                        Title = $"重选次数 +{appliedIncrease}",
                        DisplayName = "重选赐福",
                        Description = $"本次探索的战斗升级奖励重选次数 +{appliedIncrease}。\n" +
                            $"剩余次数：{receipt.ValueBefore} → {receipt.ValueAfter}。",
                        PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                            receipt.Kind, receipt.PresentationId),
                        Artwork = RewardArtworkCatalog.ResolveInteractionBuff(
                            receipt.Kind),
                        Quantity = appliedIncrease,
                        ValueBefore = receipt.ValueBefore,
                        ValueAfter = receipt.ValueAfter,
                        Rarity = receipt.Rarity,
                        Accent = new Color(0.54f, 0.48f, 0.78f, 1f)
                    };
                case LevelInteractionEffectKind.GrantExperience:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.Stat,
                        Title = "获得经验",
                        DisplayName = $"+{receipt.Amount} 经验",
                        Description = DetailOr(receipt, $"获得 {receipt.Amount} 点经验。"),
                        PresentationId = RewardArtworkCatalog.ExperiencePresentationId,
                        Artwork = RewardArtworkCatalog.Resolve(RewardArtworkCatalog.ExperiencePresentationId),
                        Quantity = receipt.Amount,
                        ValueBefore = receipt.ValueBefore,
                        ValueAfter = receipt.ValueAfter,
                        Rarity = receipt.Rarity
                    };
                case LevelInteractionEffectKind.RecordStoryClue:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.Generic,
                        Title = "发现线索",
                        DisplayName = SummaryOr(receipt, "星痕残片"),
                        Description = DetailOr(
                            receipt, "已记录一条本次探索的剧情线索。"),
                        PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                            receipt.Kind, receipt.PresentationId),
                        Quantity = receipt.Amount,
                        Rarity = receipt.Rarity,
                        Accent = new Color(0.42f, 0.62f, 0.78f, 1f)
                    };
                default:
                    return new RewardFeedbackItem
                    {
                        ReceiptId = receipt.EffectId,
                        Kind = RewardFeedbackKind.Generic,
                        Title = "获得奖励",
                        DisplayName = SummaryOr(receipt, "获得奖励"),
                        Description = DetailOr(
                            receipt, receipt.PresentationId),
                        PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                            receipt.Kind, receipt.PresentationId),
                        Quantity = receipt.Amount,
                        Rarity = receipt.Rarity
                    };
            }
        }

        /// <summary>Typed conversion presentation; the source receipt stays ConvertCard.</summary>
        public static RewardFeedbackItem FromBaseManaConversion(LevelInteractionReceipt receipt)
            => FromBaseManaGain(receipt);

        private static RewardFeedbackItem FromBaseManaGain(LevelInteractionReceipt receipt)
        {
            int increase = Math.Max(0, receipt.ValueAfter - receipt.ValueBefore);
            return new RewardFeedbackItem
            {
                ReceiptId = receipt.EffectId,
                Kind = RewardFeedbackKind.BaseMana,
                Title = $"基础灵力 +{increase}",
                DisplayName = "聚灵台",
                Description = $"本次探索后续战斗，每回合基础灵力 +{increase}。",
                ProgressText = $"基础灵力加成：{receipt.ValueBefore} → {receipt.ValueAfter}",
                PresentationId = RewardArtworkCatalog.ResolveInteractionPresentationId(
                    LevelInteractionEffectKind.AddBaseMana, null),
                Artwork = null, // The shared blue-qi preview is the subject, never a HUD icon.
                Quantity = increase,
                ValueBefore = receipt.ValueBefore,
                ValueAfter = receipt.ValueAfter,
                Rarity = receipt.Rarity,
                Accent = new Color(0.40f, 0.72f, 0.78f, 1f)
            };
        }

        private static string SummaryOr(
            LevelInteractionReceipt receipt,
            string fallback)
        {
            return string.IsNullOrWhiteSpace(receipt.SummaryText)
                ? fallback
                : receipt.SummaryText;
        }

        private static string DetailOr(
            LevelInteractionReceipt receipt,
            string fallback)
        {
            return string.IsNullOrWhiteSpace(receipt.DetailText)
                ? fallback
                : receipt.DetailText;
        }

        private static RewardRarity ResolveSealRarity(LingyinGrade grade)
        {
            return grade switch
            {
                LingyinGrade.Uncommon => RewardRarity.Uncommon,
                LingyinGrade.Rare => RewardRarity.Rare,
                LingyinGrade.VeryRare => RewardRarity.VeryRare,
                LingyinGrade.UltraRare => RewardRarity.UltraRare,
                _ => RewardRarity.Common
            };
        }
    }

    public sealed class RewardUtilityAction
    {
        public RewardUtilityActionKind Kind { get; }
        public string Label { get; }
        public Action Execute { get; }

        public RewardUtilityAction(
            RewardUtilityActionKind kind,
            string label,
            Action execute)
        {
            Kind = kind;
            Label = label;
            Execute = execute;
        }
    }

    /// <summary>
    /// 奖励操作的统一显隐门禁。只有同时解锁且已有实际处理器的操作才会渲染，
    /// 避免先展示尚未接通的道藏能力。
    /// </summary>
    public static class RewardUtilityActionFactory
    {
        public static List<RewardUtilityAction> CreateCashOut(
            RewardUtilityAvailability availability,
            int previewAmount,
            Action onCashOut)
        {
            var actions = new List<RewardUtilityAction>(1);
            AddIfAvailable(
                actions,
                availability.CanAbandon,
                RewardUtilityActionKind.CashOut,
                $"化为灵玉 · +{Math.Max(0, previewAmount)}",
                onCashOut);
            return actions;
        }

        public static List<RewardUtilityAction> Create(
            RewardUtilityAvailability availability,
            Action onRefresh,
            Action onAbandon,
            Action onExclude)
        {
            var actions = new List<RewardUtilityAction>(3);
            AddIfAvailable(
                actions,
                availability.CanRefresh,
                RewardUtilityActionKind.Refresh,
                "刷新",
                onRefresh);
            AddIfAvailable(
                actions,
                availability.CanAbandon,
                RewardUtilityActionKind.Abandon,
                "放弃",
                onAbandon);
            AddIfAvailable(
                actions,
                availability.CanExclude,
                RewardUtilityActionKind.Exclude,
                "排除",
                onExclude);
            return actions;
        }

        private static void AddIfAvailable(
            ICollection<RewardUtilityAction> actions,
            bool available,
            RewardUtilityActionKind kind,
            string label,
            Action execute)
        {
            if (!available || execute == null)
                return;
            actions.Add(new RewardUtilityAction(kind, label, execute));
        }
    }
}
