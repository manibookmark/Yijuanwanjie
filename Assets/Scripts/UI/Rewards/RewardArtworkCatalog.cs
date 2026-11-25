using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 把稳定奖励展示 ID 解析为单一运行时本体图。调用方不使用显示名称或
    /// 文件名建立关联，同一 Sprite 可同时用于地图节点与奖励模板。
    /// 稳定映射与缓存统一由目录持有；缓存只由
    /// 本类的 Resolve/ClearCache 写入，不承接奖励规则或面板生命周期。
    /// </summary>
    public static class RewardArtworkCatalog
    {
        public const string JiudiePresentationId =
            "reward.reveal.relic.jiudie.v1";
        public const string JiudieResourcePath =
            "Sprites/UI/Rewards/jiudie-relic";
        public const string WuxingDiscPresentationId =
            "reward.reveal.relic.wuxing-disc.v1";
        public const string WuxingDiscResourcePath =
            "Sprites/UI/Rewards/wuxing-disc";
        public const string YuyinPresentationId =
            "reward.reveal.relic.yuyin.v1";
        public const string YuyinResourcePath =
            "Sprites/UI/Rewards/yuyin-relic";
        public const string DoubleDamageSealPresentationId =
            "reward.reveal.seal.double-damage.common.v1";
        public const string DoubleDamageSealResourcePath =
            "Sprites/UI/Rewards/double-damage-seal-common";
        public const string EvolutionSealPresentationId =
            "reward.reveal.seal.huaxing.immortal.v1";
        public const string EvolutionSealResourcePath =
            "Sprites/UI/Rewards/huaxing-seal-immortal-v1";
        public const string DrawSealPresentationId =
            "reward.reveal.seal.draw.rare.v1";
        public const string DrawSealResourcePath =
            "Sprites/UI/Rewards/draw-seal-rare-v1";
        public const string ArmorSealPresentationId =
            "reward.reveal.seal.armor.common.v1";
        public const string ArmorSealResourcePath =
            "Sprites/UI/Rewards/armor-seal-common-v1";
        public const string AmountSealPresentationId =
            "reward.reveal.seal.amount.rare.v1";
        public const string AmountSealResourcePath =
            "Sprites/UI/Rewards/amount-seal-rare-v1";
        public const string BengLieSealPresentationId =
            "reward.reveal.seal.benglie.common.v1";
        public const string BengLieSealResourcePath =
            "Sprites/UI/Rewards/benglie-seal-common-v1";
        public const string ReturnSealPresentationId =
            "reward.reveal.seal.return.rare.v1";
        public const string ReturnSealResourcePath =
            "Sprites/UI/Rewards/return-seal-rare-v1";
        public const string RefundSealPresentationId =
            "reward.reveal.seal.refund.uncommon.v1";
        public const string RefundSealResourcePath =
            "Sprites/UI/Rewards/refund-seal-uncommon-v1";
        public const string NdujaSealPresentationId =
            "reward.reveal.seal.nduja.uncommon.v1";
        public const string NdujaSealResourcePath =
            "Sprites/UI/Rewards/nduja-seal-uncommon-v1";
        public const string YellowTriggerSealPresentationId =
            "reward.reveal.seal.yellow-trigger.uncommon.v1";
        public const string YellowTriggerSealResourcePath =
            "Sprites/UI/Rewards/yellow-trigger-seal-uncommon-v1";
        public const string MidasSealPresentationId =
            "reward.reveal.seal.midas.very-rare.v1";
        public const string MidasSealResourcePath =
            "Sprites/UI/Rewards/midas-seal-very-rare-v1";
        public const string ManaRebateSealPresentationId =
            "reward.reveal.seal.mana-rebate.ultra-rare.v1";
        public const string ManaRebateSealResourcePath =
            "Sprites/UI/Rewards/mana-rebate-seal-ultra-rare-v1";
        public const string LeaderSealPresentationId =
            "reward.reveal.seal.leader.very-rare.v1";
        public const string LeaderSealResourcePath =
            "Sprites/UI/Rewards/leader-seal-very-rare-v1";
        public const string ArmorStrikeSealPresentationId =
            "reward.reveal.seal.armor-strike.very-rare.v1";
        public const string ArmorStrikeSealResourcePath =
            "Sprites/UI/Rewards/armor-strike-seal-very-rare-v1";
        public const string MightSealPresentationId = "reward.reveal.seal.might.common.v1";
        public const string MightSealResourcePath = "Sprites/UI/Rewards/might-seal-common-v1";
        public const string MugSealPresentationId = "reward.reveal.seal.mug.common.v1";
        public const string MugSealResourcePath = "Sprites/UI/Rewards/mug-seal-common-v1";
        public const string CopySealPresentationId = "reward.reveal.seal.copy.very-rare.v1";
        public const string CopySealResourcePath = "Sprites/UI/Rewards/copy-seal-very-rare-v1";
        public const string TripleDamageSealPresentationId = "reward.reveal.seal.triple-damage.rare.v1";
        public const string TripleDamageSealResourcePath = "Sprites/UI/Rewards/triple-damage-seal-rare-v1";
        public const string RetainSealPresentationId = "reward.reveal.seal.retain.uncommon.v1";
        public const string RetainSealResourcePath = "Sprites/UI/Rewards/retain-seal-uncommon-v1";
        public const string ReverseComboSealPresentationId = "reward.reveal.seal.reverse-combo.rare.v1";
        public const string ReverseComboSealResourcePath = "Sprites/UI/Rewards/reverse-combo-seal-rare-v1";
        public const string JadePresentationId =
            "reward.reveal.jade.v1";
        public const string JadeResourcePath =
            "Sprites/Maps/jade-node";
        public const string PolentirPresentationId =
            "reward.reveal.relic.polentir.v1";
        public const string PolentirResourcePath =
            "Sprites/UI/Rewards/guanxing-tai-relic-v1";
        public const string TianjiFacilityPresentationId =
            "reward.reveal.facility.tianjige.v1";
        public const string TianjiFacilityResourcePath =
            "Sprites/UI/Rewards/guanxing-tai-building-v2";
        public const string MightPresentationId =
            "reward.reveal.stat.might.v1";
        public const string MightResourcePath =
            "Sprites/UI/Battle/BuffIcons/might";
        public const string HandPresentationId =
            "reward.reveal.stat.hand.v1";
        public const string ManaPresentationId =
            "reward.reveal.stat.mana.v1";
        public const string ExperiencePresentationId =
            "reward.reveal.stat.experience.v1";
        public const string ExperienceResourcePath =
            "Sprites/UI/Battle/BuffIcons/growth";
        public const string HandResourcePath =
            "Sprites/UI/Battle/BuffIcons/hand";
        public const string ManaResourcePath =
            "Sprites/UI/Daozang/glyphs/mana";
        public const string ArmorResourcePath =
            "Sprites/UI/Daozang/glyphs/armor";
        public const string AreaResourcePath =
            "Sprites/UI/Battle/BuffIcons/area";
        public const string RerollResourcePath =
            "Sprites/UI/Daozang/glyphs/reroll";
        public const string RecoveryResourcePath =
            "Sprites/UI/MapRewards/health-life-drop-v1";
        public const string JadeBuffResourcePath =
            "Sprites/UI/Battle/BuffIcons/greed";

        public const string ArmorPresentationId = "reward.reveal.stat.armor.v1";
        public const string AreaPresentationId = "reward.reveal.stat.area.v1";
        public const string RerollPresentationId = "reward.reveal.stat.reroll.v1";
        public const string RecoveryPresentationId = "reward.reveal.stat.recovery.v1";

        private static readonly Dictionary<string, string> ResourcePaths =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { JiudiePresentationId, JiudieResourcePath },
                { WuxingDiscPresentationId, WuxingDiscResourcePath },
                { YuyinPresentationId, YuyinResourcePath },
                { DoubleDamageSealPresentationId, DoubleDamageSealResourcePath },
                { EvolutionSealPresentationId, EvolutionSealResourcePath },
                { DrawSealPresentationId, DrawSealResourcePath },
                { ArmorSealPresentationId, ArmorSealResourcePath },
                { AmountSealPresentationId, AmountSealResourcePath },
                { BengLieSealPresentationId, BengLieSealResourcePath },
                { ReturnSealPresentationId, ReturnSealResourcePath },
                { RefundSealPresentationId, RefundSealResourcePath },
                { NdujaSealPresentationId, NdujaSealResourcePath },
                { YellowTriggerSealPresentationId, YellowTriggerSealResourcePath },
                { MidasSealPresentationId, MidasSealResourcePath },
                { ManaRebateSealPresentationId, ManaRebateSealResourcePath },
                { LeaderSealPresentationId, LeaderSealResourcePath },
                { ArmorStrikeSealPresentationId, ArmorStrikeSealResourcePath },
                { MightSealPresentationId, MightSealResourcePath },
                { MugSealPresentationId, MugSealResourcePath },
                { CopySealPresentationId, CopySealResourcePath },
                { TripleDamageSealPresentationId, TripleDamageSealResourcePath },
                { RetainSealPresentationId, RetainSealResourcePath },
                { ReverseComboSealPresentationId, ReverseComboSealResourcePath },
                { JadePresentationId, JadeResourcePath },
                { PolentirPresentationId, PolentirResourcePath },
                { TianjiFacilityPresentationId, TianjiFacilityResourcePath },
                { MightPresentationId, MightResourcePath },
                { HandPresentationId, HandResourcePath },
                { ManaPresentationId, ManaResourcePath },
                { "reward.reveal.mana.v1", ManaResourcePath },
                { "reward.reveal.base-mana.v1", ManaResourcePath },
                { ExperiencePresentationId, ExperienceResourcePath },
                { ArmorPresentationId, ArmorResourcePath },
                { AreaPresentationId, AreaResourcePath },
                { RerollPresentationId, RerollResourcePath },
                { RecoveryPresentationId, RecoveryResourcePath },
            };
        private static readonly Dictionary<string, Sprite> Sprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        /// <summary>Preserve supplied art, then use the stable presentation identity.
        /// Currency and healing retain their shared subject even without a presentation identity.</summary>
        public static Sprite ResolveRewardArtwork(RewardFeedbackItem item)
        {
            if (item == null) return null;
            if (item.Artwork != null) return item.Artwork;
            Sprite artwork = Resolve(item.PresentationId);
            if (artwork != null) return artwork;
            return item.Kind switch
            {
                RewardFeedbackKind.Jade => Resolve(JadePresentationId),
                RewardFeedbackKind.CurrentHealth => Resolve(RecoveryPresentationId),
                _ => null
            };
        }

        public static string ResolveRelicPresentationId(string contentId)
        {
            return contentId switch
            {
                RelicContentIds.Jiudie => JiudiePresentationId,
                RelicContentIds.WuxingDisc => WuxingDiscPresentationId,
                RelicContentIds.Yuyin => YuyinPresentationId,
                RelicContentIds.Polentir => PolentirPresentationId,
                RelicContentIds.GuanxingTai => PolentirPresentationId,
                _ => null
            };
        }

        public static Sprite Resolve(string presentationId)
        {
            if (string.IsNullOrWhiteSpace(presentationId) ||
                !ResourcePaths.TryGetValue(presentationId, out string path)) return null;
            if (Sprites.TryGetValue(presentationId, out Sprite sprite) && sprite != null)
                return sprite;
            sprite = Resources.Load<Sprite>(path);
            if (sprite != null) Sprites[presentationId] = sprite;
            return sprite;
        }

        public static bool CanResolvePresentationId(string presentationId)
            => Resolve(presentationId) != null;

        public static string ResolveInteractionPresentationId(LevelInteractionEffect effect)
            => effect == null ? null : ResolveInteractionPresentationId(
                effect.kind, effect.presentationId);

        public static string ResolveInteractionPresentationId(
            LevelInteractionEffectKind kind, string requestedPresentationId)
        {
            // Other presentation domains (e.g. character reveals) have their
            // own contracts. Only reward-subject overrides belong here.
            if (!string.IsNullOrWhiteSpace(requestedPresentationId) &&
                requestedPresentationId.StartsWith("reward.reveal.", StringComparison.Ordinal))
                return requestedPresentationId;
            return GetDefaultInteractionPresentationId(kind);
        }

        private static string GetDefaultInteractionPresentationId(LevelInteractionEffectKind kind)
        {
            return kind switch
            {
                LevelInteractionEffectKind.AddBaseMana => ManaPresentationId,
                LevelInteractionEffectKind.ManaStatue => ManaPresentationId,
                LevelInteractionEffectKind.ManaOffering => ManaPresentationId,
                LevelInteractionEffectKind.GrantJade => JadePresentationId,
                LevelInteractionEffectKind.AddMight => MightPresentationId,
                LevelInteractionEffectKind.AddHandLimit => HandPresentationId,
                LevelInteractionEffectKind.AddBaseArmor => ArmorPresentationId,
                LevelInteractionEffectKind.AddArea => AreaPresentationId,
                LevelInteractionEffectKind.AddRewardReroll => RerollPresentationId,
                LevelInteractionEffectKind.HealCurrentHealth => RecoveryPresentationId,
                LevelInteractionEffectKind.ChickenOffering => RecoveryPresentationId,
                LevelInteractionEffectKind.GrantExperience => ExperiencePresentationId,
                _ => null
            };
        }

        public static Sprite ResolveInteractionBuff(LevelInteractionEffectKind kind)
            => Resolve(GetDefaultInteractionPresentationId(kind));

        public static void ClearCache() => Sprites.Clear();
    }
}
