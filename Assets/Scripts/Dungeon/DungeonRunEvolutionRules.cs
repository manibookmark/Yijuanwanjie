using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 历史地图精炼的兼容边界。正式进化只由主卡与化形印完成；旧 RefineCard
    /// 节点若仍存在，也只匹配主卡并忽略旧副卡字段。主卡消耗一个空印位并继承
    /// 其余印位与灵印，所有产物只存在于当前 Run。
    /// </summary>
    internal static class DungeonRunEvolutionRules
    {
        internal static List<DungeonRunInteractionOption> GetOptions(
            IReadOnlyList<DungeonRunCardSaveData> cards, DungeonRunInteractionCatalog catalog)
            => GetOptions(cards, catalog, null);

        /// <summary>
        /// 当节点锁定一个配方时，只暴露该稳定配方 ID 的合法实例组合；留空时
        /// 保持旧有的通用精炼菜单，不让 F1 的作者约束泄漏到其他节点。
        /// </summary>
        internal static List<DungeonRunInteractionOption> GetOptions(
            IReadOnlyList<DungeonRunCardSaveData> cards,
            DungeonRunInteractionCatalog catalog,
            LevelInteractionFixedEvolutionContract fixedEvolution)
        {
            var options = new List<DungeonRunInteractionOption>();
            foreach (DungeonRunEvolutionRecipe recipe in catalog.Recipes)
            {
                if (recipe == null || string.IsNullOrWhiteSpace(recipe.ContentId) ||
                    !catalog.CanEvolveTo(recipe.ResultCardContentId) ||
                    !MatchesFixedContract(recipe, fixedEvolution)) continue;
                foreach (DungeonRunCardSaveData primary in cards)
                {
                    if (!Same(primary.contentId, recipe.PrimaryCardContentId)) continue;
                    CardConfig source = catalog.FindCard(primary.contentId);
                    if (source == null || Capacity(primary, source) <= (primary.seals?.Count ?? 0)) continue;
                    CardConfig result = catalog.FindCard(recipe.ResultCardContentId);
                    if (result == null) continue;
                    options.Add(new DungeonRunInteractionOption
                    {
                        Id = $"refine/{recipe.ContentId}/{primary.runtimeInstanceId}",
                        CardContentId = result.contentId,
                        CardRuntimeInstanceId = primary.runtimeInstanceId,
                        SecondaryRuntimeInstanceId = 0,
                        Title = result.cardName,
                        Description = $"{source.cardName} → {result.cardName}；占用1个印位，保留已有灵印。"
                    });
                }
            }
            return options;
        }

        internal static bool TryValidateFixedContract(
            DungeonRunInteractionCatalog catalog,
            LevelInteractionFixedEvolutionContract contract,
            out string error)
        {
            error = null;
            if (contract == null || !contract.IsSpecified)
                return true;
            if (catalog?.Recipes == null)
            {
                error = "固定精炼配方尚未进入本次 Run 的内容目录。";
                return false;
            }

            DungeonRunEvolutionRecipe found = null;
            foreach (DungeonRunEvolutionRecipe recipe in catalog.Recipes)
            {
                if (recipe == null || !Same(recipe.ContentId,
                        contract.recipeContentId))
                    continue;
                if (found != null)
                {
                    error = $"地图精炼配方 contentId 重复：{contract.recipeContentId}。";
                    return false;
                }
                found = recipe;
            }
            if (found == null)
            {
                error = "此节点指定的精炼配方尚未进入本次 Run 的内容目录。";
                return false;
            }
            if (!MatchesFixedContract(found, contract))
            {
                error = "此节点的固定精炼分支与本次 Run 的演化目录不一致。";
                return false;
            }
            return true;
        }

        private static bool MatchesFixedContract(
            DungeonRunEvolutionRecipe recipe,
            LevelInteractionFixedEvolutionContract contract)
        {
            return contract == null || !contract.IsSpecified ||
                (Same(recipe.ContentId, contract.recipeContentId) &&
                 Same(recipe.PrimaryCardContentId,
                    contract.primaryCardContentId) &&
                 Same(recipe.ResultCardContentId,
                    contract.resultCardContentId));
        }

        internal static void Apply(DungeonRunInteractionDraft draft,
            DungeonRunInteractionOption option, DungeonRunInteractionCatalog catalog)
        {
            DungeonRunCardSaveData primary = draft.Cards.Find(card => card.runtimeInstanceId == option.CardRuntimeInstanceId);
            var references = DungeonRunCardCodec.CreateResolver(catalog.Cards);
            CardConfig host = null, evolved = null;
            try
            {
                host = DungeonRunCardCodec.Restore(primary, references);
                int capacity = CardSealRules.GetSlotCapacity(host);
                evolved = CardInstanceFactory.RestoreReplacement(host, catalog.FindCard(option.CardContentId), 0, host.seals);
                evolved.gemSlots = capacity - 1;
                draft.Cards[draft.Cards.IndexOf(primary)] = DungeonRunCardCodec.Capture(evolved, references);
            }
            finally { DungeonRunCardCodec.Release(evolved); DungeonRunCardCodec.Release(host); }
        }

        internal static int Capacity(DungeonRunCardSaveData card, CardConfig source)
            => card.hasGemSlotCapacity ? card.gemSlotCapacity : CardSealRules.GetSlotCapacity(source);

        private static bool Same(string left, string right)
            => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
