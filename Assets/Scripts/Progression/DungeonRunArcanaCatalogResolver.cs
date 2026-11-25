using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Maps established Arcana content and explicit account/relic qualification
    /// into a detached Run pool. It owns no choices or account writes. Randomazzo
    /// is separate from Polentir; missing production relic content fails closed.
    /// Baishu remains an entry-only frozen rule, never filler for three choices.
    /// </summary>
    public static class DungeonRunArcanaCatalogResolver
    {
        public const string PoolContentId = "pool.xiyou.arcana.default.v1";
        public const string LegacyAcquisitionRelicContentId = "legacy.relic.common.192a46fc";

        /// <summary>
        /// Canonical content-to-rule boundary for both new offers and restored
        /// battle projections. Dungeon validates acquisition proof; this module
        /// rejects unknown or mismatched authored identities before rules execute.
        /// </summary>
        public static void RequireImplementedDefinition(DungeonRunArcanaDefinition definition)
        {
            if (definition != null &&
                ((definition.ContentId == ArcanaContentIds.ShieldBash &&
                  definition.Rule == DungeonRunArcanaRule.ShieldBash) ||
                 (definition.ContentId == ArcanaContentIds.ExperimentalMedicine &&
                  definition.Rule == DungeonRunArcanaRule.ExperimentalMedicine))) return;
            throw new InvalidOperationException("局内天机身份与正式接入规则不匹配：" + definition?.ContentId);
        }

        public static DungeonRunArcanaCatalog Resolve(MetaProgression account,
            IReadOnlyList<ArcanaConfig> configs, RelicConfig acquisitionRelic)
        {
            bool unlocked = account != null && acquisitionRelic != null &&
                acquisitionRelic.enabled && acquisitionRelic.type == RelicType.ArcanaFinder &&
                string.Equals(acquisitionRelic.contentId, LegacyAcquisitionRelicContentId,
                    StringComparison.Ordinal) && account.IsRelicUnlocked(acquisitionRelic.contentId) &&
                account.HasRelicType(RelicType.ArcanaFinder);
            return ResolveCandidates(account, configs, unlocked);
        }

        /// <summary>The production package is the author source for relics; no synthetic asset is needed.</summary>
        public static DungeonRunArcanaCatalog ResolveFromContent(MetaProgression account,
            IReadOnlyList<ArcanaConfig> configs, RelicData acquisitionRelic)
        {
            bool unlocked = account != null && acquisitionRelic != null &&
                acquisitionRelic.relicType == (int)RelicType.ArcanaFinder &&
                string.Equals(acquisitionRelic.contentId, LegacyAcquisitionRelicContentId,
                    StringComparison.Ordinal) && account.IsRelicUnlocked(acquisitionRelic.contentId) &&
                account.HasRelicType(RelicType.ArcanaFinder);
            return ResolveCandidates(account, configs, unlocked);
        }

        private static DungeonRunArcanaCatalog ResolveCandidates(MetaProgression account,
            IReadOnlyList<ArcanaConfig> configs, bool unlocked)
        {
            var eligible = new List<DungeonRunArcanaDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ArcanaConfig config in configs ?? Array.Empty<ArcanaConfig>())
            {
                if (config == null || string.IsNullOrWhiteSpace(config.contentId))
                    throw new InvalidOperationException("天机候选缺少稳定内容身份。");
                if (!seen.Add(config.contentId))
                    throw new InvalidOperationException("天机候选存在重复稳定ID：" + config.contentId);
                if (config.contentId == ArcanaContentIds.BaishuZhoutian) continue;
                DungeonRunArcanaRule rule;
                if (config.contentId == ArcanaContentIds.ShieldBash && config.effect == ArcanaEffect.ShieldBash)
                    rule = DungeonRunArcanaRule.ShieldBash;
                else if (config.contentId == ArcanaContentIds.ExperimentalMedicine &&
                    config.effect == ArcanaEffect.ExperimentalMedicine)
                    rule = DungeonRunArcanaRule.ExperimentalMedicine;
                else
                    throw new InvalidOperationException("天机ID与已接入局内效果不匹配：" + config.contentId);
                var definition = new DungeonRunArcanaDefinition(config.contentId, rule);
                RequireImplementedDefinition(definition);
                if (account?.IsArcanaUnlocked(config.contentId) == true)
                    eligible.Add(definition);
            }
            return new DungeonRunArcanaCatalog(PoolContentId, eligible.AsReadOnly(), unlocked,
                account?.hasEquippedArcanaEffect == true ? account.equippedArcana : null);
        }
    }
}
