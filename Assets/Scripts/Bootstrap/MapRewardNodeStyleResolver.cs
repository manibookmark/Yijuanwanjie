using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Map;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// One map reward type-to-style contract for runtime and authoring tools.
    /// Uses the effective Run payload, so source-event projections also get
    /// their real subject. No display-name, floor-ID or node-suffix matching.
    /// Relics and directly granted seals retain their approved authored art.
    /// </summary>
    public static class MapRewardNodeStyleResolver
    {
        private const string Art = "Sprites/UI/MapRewards/";
        private const string Existing = "Sprites/UI/Rewards/";
        private static readonly Dictionary<string, Sprite> Sprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static Dictionary<string, MapNodeVisualStyle> Resolve(
            IReadOnlyList<LevelNode> nodes, MapPreviewProfile map)
        {
            var result = new Dictionary<string, MapNodeVisualStyle>(StringComparer.Ordinal);
            var logical = new Dictionary<string, LevelNode>(StringComparer.Ordinal);
            foreach (LevelNode node in nodes)
                if (node != null) logical[node.contentId] = node;
            foreach (MapPreviewNode visual in map.nodes)
            {
                if (visual == null || !logical.TryGetValue(visual.interactionGroupId, out LevelNode node))
                    continue;
                MapNodeVisualStyle style = ResolveStyle(node);
                if (style != null) result[visual.contentId] = style;
            }
            return result;
        }

        public static MapNodeVisualStyle ResolveStyle(LevelNode node)
        {
            // Floor exits share the established Jade subject. Authored placeholder
            // sprites must never make an exit look like another encounter.
            if (node?.nodeType == LevelNodeType.Exit)
                return SpriteStyle("jade", "Sprites/Maps/jade-node", MapRewardMotionKind.QuietFloat);
            LevelInteractionEffect effect = Effect(node);
            if (effect == null) return null;
            switch (effect.kind)
            {
                case LevelInteractionEffectKind.HealCurrentHealth:
                    return SpriteStyle("life", RewardArtworkCatalog.RecoveryResourcePath, MapRewardMotionKind.LifePulse);
                case LevelInteractionEffectKind.AddBaseMana:
                    // Direct supply and card conversion share the paper platform
                    // and the existing blue mana spirit.
                    return new MapNodeVisualStyle("map.reward.mana.v1",
                        MapPreviewVisualKind.ManaPlatform, null, 0.78f, MapRewardMotionKind.None);
                case LevelInteractionEffectKind.GrantJade:
                    return SpriteStyle("jade", "Sprites/Maps/jade-node", MapRewardMotionKind.QuietFloat);
                case LevelInteractionEffectKind.AddMight:
                    return SpriteStyle("might", Art + "blessing-might-v1");
                case LevelInteractionEffectKind.AddArea:
                    return SpriteStyle("area", Art + "blessing-area-v1");
                case LevelInteractionEffectKind.AddHandLimit:
                    return SpriteStyle("draw", Art + "blessing-draw-v1");
                case LevelInteractionEffectKind.AddRewardReroll:
                    return SpriteStyle("reroll", Art + "blessing-reroll-v1");
                case LevelInteractionEffectKind.AddBaseArmor:
                    return SpriteStyle("armor", Art + "blessing-armor-v1");
                case LevelInteractionEffectKind.GrantCard:
                case LevelInteractionEffectKind.ChooseCard:
                    return SpriteStyle("cards", Existing + "card-cache-v1");
                case LevelInteractionEffectKind.ChooseSeal:
                    return SpriteStyle("seals", Art + "seal-cache-open-v1");
                case LevelInteractionEffectKind.GemChest:
                    return effect.sealRewardPoolId == DungeonRunGemChests.EvoStatueOfferContractId
                        ? SpriteStyle("refine", Art + "evolution-statue-v1", size: 1.1f)
                        : SpriteStyle("seals", Art + "seal-cache-open-v1");
                case LevelInteractionEffectKind.ConvertCard:
                    if (IsManaConversion(effect))
                        return new MapNodeVisualStyle("map.facility.mana-conversion.v1",
                            MapPreviewVisualKind.ManaConversion, null, 0.78f,
                            MapRewardMotionKind.None);
                    return new MapNodeVisualStyle("map.facility.return.v1", MapPreviewVisualKind.Sprite,
                        Require(Existing + "guiyuan-stele"), 1.1f, MapRewardMotionKind.None,
                        Resources.Load<GameObject>("VfxProfiles/Map/GuiyuanStelePrototype"));
                case LevelInteractionEffectKind.RefineCard:
                    return SpriteStyle("refine", Art + "evolution-statue-v1", size: 1.1f);
                case LevelInteractionEffectKind.CrystalThrowing:
                    return SpriteStyle("crystal", Art + "crystal-node-v1", size: 1.05f);
                case LevelInteractionEffectKind.AbandonedCart:
                    return SpriteStyle("cart", Art + "abandoned-cart-v1", size: 1.1f);
                case LevelInteractionEffectKind.ManaStatue:
                    return SpriteStyle("mana-statue", Art + "mana-statue-v1", size: 1.1f);
                case LevelInteractionEffectKind.ManaOffering:
                case LevelInteractionEffectKind.ChickenOffering:
                case LevelInteractionEffectKind.CardStatOffering:
                    return SpriteStyle("offering", Art + "offering-altar-v1", size: 1.1f);
                case LevelInteractionEffectKind.DuplicateOffering:
                    return SpriteStyle("rubbing", Art + "tuoyin-node-v1", size: 1.1f);
                case LevelInteractionEffectKind.GunForHire:
                case LevelInteractionEffectKind.CrawlerInvitation:
                    return SpriteStyle("spirit-invitation", Art + "qingling-node-v1", size: 1.1f);
                case LevelInteractionEffectKind.ChooseArcana:
                    return SpriteStyle("arcana", Existing + "guanxing-tai-building-v2", size: 1.1f);
                // Dedicated facilities are typed independently. They are not
                // silently turned into loose Jade, cards or an XP pickup.
                default:
                    return null;
            }
        }

        public static string ResolveName(LevelNode node, bool completed = false)
        {
            if (node == null) return null;
            if (node.nodeType == LevelNodeType.Exit) return "下一层入口";
            LevelInteractionEffect effect = Effect(node);
            if (effect == null) return null;
            switch (effect.kind)
            {
                case LevelInteractionEffectKind.HealCurrentHealth: return "生命补给";
                case LevelInteractionEffectKind.AddBaseMana: return "聚灵台";
                case LevelInteractionEffectKind.GrantJade: return "灵玉";
                case LevelInteractionEffectKind.AddMight: return "锋锐赐福";
                case LevelInteractionEffectKind.AddArea: return "裂空赐福";
                case LevelInteractionEffectKind.AddHandLimit: return "抽牌赐福";
                case LevelInteractionEffectKind.AddRewardReroll: return "重选赐福";
                case LevelInteractionEffectKind.AddBaseArmor: return "护甲赐福";
                case LevelInteractionEffectKind.GrantCard:
                case LevelInteractionEffectKind.ChooseCard: return "卡牌";
                case LevelInteractionEffectKind.ChooseSeal:
                    return "灵印";
                case LevelInteractionEffectKind.GemChest:
                    return effect.sealRewardPoolId == DungeonRunGemChests.EvoStatueOfferContractId
                        ? "化形台" : effect.sealRewardPoolId == DungeonRunGemChests.SilverOfferContractId
                            ? "银纹灵印匣" : effect.sealRewardPoolId == DungeonRunGemChests.GoldOfferContractId
                                ? "金纹灵印匣" : "灵印";
                case LevelInteractionEffectKind.ConvertCard:
                    return IsManaConversion(effect)
                        ? "聚灵台"
                        : "归元碑";
                case LevelInteractionEffectKind.RefineCard: return "化形台";
                case LevelInteractionEffectKind.ManaStatue: return "灵气雕像";
                case LevelInteractionEffectKind.ManaOffering: return "灵气供奉台";
                case LevelInteractionEffectKind.ChickenOffering: return "食物供奉台";
                case LevelInteractionEffectKind.CardStatOffering: return "属性供奉台";
                case LevelInteractionEffectKind.DuplicateOffering: return "拓印处";
                case LevelInteractionEffectKind.GunForHire: return "请灵台";
                case LevelInteractionEffectKind.CrawlerInvitation: return "请灵台";
                case LevelInteractionEffectKind.ChooseArcana: return "天机台";
                case LevelInteractionEffectKind.CrystalThrowing: return "晶体";
                case LevelInteractionEffectKind.AbandonedCart: return "弃车";
                default: return effect.summaryText;
            }
        }

        private static LevelInteractionEffect Effect(LevelNode node)
            => node?.nodeType == LevelNodeType.Interaction &&
               node.completionEffects?.Count > 0 ? node.completionEffects[0] : null;

        /// <summary>
        /// Composition owns the common visual identity for fixed mana conversion;
        /// selection eligibility and the awarded amount remain in Dungeon.
        /// </summary>
        internal static bool IsManaConversion(RunCardConversionRuleProfile profile)
            => profile != null && profile.RequiresManaPickup;

        internal static bool IsManaConversion(LevelInteractionEffect effect)
            => effect != null && effect.kind == LevelInteractionEffectKind.ConvertCard &&
               RunCardConversionRuleCatalog.TryGet(effect.targetContentId, out var profile) &&
               IsManaConversion(profile);

        private static MapNodeVisualStyle SpriteStyle(string key, string path,
            MapRewardMotionKind motion = MapRewardMotionKind.None, float size = 0.9f)
            => new MapNodeVisualStyle("map.reward." + key + ".v1",
                MapPreviewVisualKind.Sprite, Require(path), size, motion,
                normalizedArtwork: path.StartsWith(Art, StringComparison.Ordinal),
                fitHitToSprite: true);

        private static Sprite Require(string path)
        {
            if (Sprites.TryGetValue(path, out Sprite sprite) && sprite != null) return sprite;
            sprite = Resources.Load<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("地图共享奖励素材缺失：" + path);
            Sprites[path] = sprite;
            return sprite;
        }
    }
}
