using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns the declared historical-income projection and purchase ledger for
    /// one isolated test account. Meta alone applies income and purchase rules;
    /// this adapter never edits saves, grants upgrade levels or models a prior Run.
    /// </summary>
    internal sealed class AgentGameProgressionAccount
    {
        internal const string G3RecipePath =
            "tools/simulation/account-recipes/g3-g2-cleared-historical-daozang-v1.json";
        internal const string G4RecipePath =
            "tools/simulation/account-recipes/g4-g3-cleared-historical-daozang-v1.json";
        internal const string G4RecipeContentId = "agent-account.g4.g3-cleared.historical-daozang.v1";
        // This case is one user-approved declaration, not an arbitrary budget
        // importer. Bind all fields (including unknown ones) before decoding.
        internal const string G4ApprovedRecipeSha256 = "e260b3572cbc40fab79d18592bf828ae1f30fa9baa0bb0e6136aef9acc980e8a";
        internal MetaProgression Account { get; }
        internal string Declaration { get; }
        internal string FingerprintPayload { get; }
        internal string SelectedHeroContentId { get; }
        internal string EquippedArcanaContentId { get; }

        private AgentGameProgressionAccount(MetaProgression account, string declaration, string fingerprint,
            string selectedHeroContentId = null, string equippedArcanaContentId = null)
        {
            Account = account; Declaration = declaration; FingerprintPayload = fingerprint;
            SelectedHeroContentId = selectedHeroContentId; EquippedArcanaContentId = equippedArcanaContentId;
        }

        internal static AgentGameProgressionAccount LoadG3()
        {
            return Build(AgentGameHostEnvironment.ReadHistoricalRecipe(G3RecipePath));
        }

        internal static AgentGameProgressionAccount Build(string recipeJson)
            => BuildEconomy(recipeJson, ParseRecipe(recipeJson, 1), TestAccountPreset.G2Cleared);

        internal static AgentGameProgressionAccount LoadG4()
            => BuildG4(AgentGameHostEnvironment.ReadHistoricalRecipe(G4RecipePath));

        internal static AgentGameProgressionAccount BuildG4(string recipeJson)
        {
            recipeJson = recipeJson != null && recipeJson.StartsWith("\ufeff", StringComparison.Ordinal)
                ? recipeJson.Substring(1) : recipeJson;
            recipeJson = recipeJson?.Replace("\r\n", "\n").Replace('\r', '\n');
            if (AgentGameTrace.Hash(recipeJson) != G4ApprovedRecipeSha256)
                throw new InvalidOperationException("G4 historical account requires the exact approved recipe; source/budget changes need a separately declared case.");
            Recipe recipe = ParseRecipe(recipeJson, 2);
            if (recipe.contentId != G4RecipeContentId || recipe.progressPreset != "G3Cleared" ||
                recipe.selectedHeroContentId != "legacy.crawler.common.5a3f3db3" ||
                recipe.equippedArcanaContentId != "arcana.xiyou.shield-bash.v1" ||
                recipe.expectedTotalIncome <= 0 || recipe.expectedTotalSpent < 0 ||
                recipe.expectedRemainingJade < 0 || string.IsNullOrWhiteSpace(recipe.sourceDeclaration))
                throw new InvalidOperationException("Invalid declared G4 historical-account contract.");
            return BuildEconomy(recipeJson, recipe, TestAccountPreset.G3Cleared);
        }

        private static Recipe ParseRecipe(string recipeJson, int schemaVersion)
        {
            Recipe recipe = JsonUtility.FromJson<Recipe>(recipeJson);
            if (recipe == null || recipe.schemaVersion != schemaVersion ||
                string.IsNullOrWhiteSpace(recipe.contentId) ||
                recipe.incomeSources == null || recipe.incomeSources.Count == 0 || recipe.purchases == null)
                throw new InvalidOperationException("Invalid historical-progression account recipe.");
            return recipe;
        }

        private static AgentGameProgressionAccount BuildEconomy(string recipeJson, Recipe recipe,
            TestAccountPreset preset)
        {
            // ReplayTutorial requires its original tutorial hero. The declared
            // campaign hero is confirmed only after the permanent blueprint.
            MetaProgression account = ProgressAccountBlueprints.Build(preset,
                "legacy.crawler.common.5a3f3db3");
            if (account.lingyu != 0 || account.totalLingyuEarned != 0 || account.powerUpLevels.Count != 0 ||
                !string.IsNullOrEmpty(account.equippedArcana) || account.hasEquippedArcanaEffect)
                throw new InvalidOperationException("Historical income requires the declared empty starting economy.");

            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            var sourceDeclarations = new List<string>();
            int totalIncome = 0;
            foreach (IncomeSource source in recipe.incomeSources)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.evidencePath) ||
                    Path.IsPathRooted(source.evidencePath) || source.evidencePath.Contains("..") ||
                    !sourceIds.Add(source.evidencePath) || source.earnedJade <= 0 || source.revision <= 0 ||
                    source.sha256 == null || source.sha256.Length != 64 ||
                    source.sha256.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')))
                    throw new InvalidOperationException("Invalid or duplicate historical income evidence.");
                totalIncome = checked(totalIncome + source.earnedJade);
                account.CreditLingyu(source.earnedJade);
                sourceDeclarations.Add(source.evidencePath + "@r" + source.revision + "=" + source.earnedJade +
                    (recipe.schemaVersion == 2 ? ",sha256=" + source.sha256 : string.Empty));
            }

            var purchases = new List<string>();
            foreach (string id in recipe.purchases)
            {
                PowerUpConfig config = DaozangProgressionRules.CreatePowerUpConfig(id);
                try
                {
                    int before = account.lingyu;
                    int level = account.GetPowerUpLevel(config);
                    int price = config.GetLevelPrice(level + 1);
                    if (DaozangProgressionRules.IsLocked(account, config.tier) || level >= config.maxLevel ||
                        price <= 0 || before < price)
                        throw new InvalidOperationException("Unavailable historical-account purchase: " + id);
                    if (!account.BuyPowerUp(config) || account.lingyu != before - price ||
                        account.GetPowerUpLevel(config) != level + 1)
                        throw new InvalidOperationException("Formal upgrade purchase did not commit: " + id);
                    purchases.Add(id + ":Lv" + level + "->" + (level + 1) +
                        ",cost=" + price + ",jade=" + before + "->" + account.lingyu);
                }
                finally { UnityEngine.Object.DestroyImmediate(config); }
            }

            if (recipe.schemaVersion == 2 && (totalIncome != recipe.expectedTotalIncome ||
                account.lingyu != recipe.expectedRemainingJade || totalIncome - account.lingyu != recipe.expectedTotalSpent))
                throw new InvalidOperationException("Current formal prices do not match the declared historical-account economy.");
            string ledger = (recipe.schemaVersion == 2 ? recipe.sourceDeclaration + "; " :
                "historical-income projection from independent completed tests (not a continuous save); ") +
                string.Join("; ", sourceDeclarations) + "; income=" + totalIncome +
                "; purchases=" + string.Join(" | ", purchases) + "; remaining jade=" + account.lingyu +
                (recipe.schemaVersion == 2
                    ? "; declared hero=" + recipe.selectedHeroContentId + "; declared equipped arcana=" + recipe.equippedArcanaContentId +
                      "; equipment is applied separately through the formal account operation; no user save/prior Run/other historical statistics imported"
                    : "; no equipped arcana/user save/prior Run build imported");
            // Prices and effect amounts join the recipe fingerprint so a changed
            // common upgrade definition cannot masquerade as the old entry state.
            string definitions = string.Join(";", DaozangProgressionRules.All.Select(definition =>
                definition.Id + ":" + definition.MaxLevel + ":" + definition.AmountPerLevel + ":" +
                definition.StatType + ":" + definition.Tier + ":" +
                string.Join(",", Enumerable.Range(1, definition.MaxLevel).Select(level =>
                    DaozangProgressionRules.GetLevelPrice(definition.Id, level).ToString(CultureInfo.InvariantCulture)))));
            return new AgentGameProgressionAccount(account, ledger, recipeJson + "\n" + definitions + "\n" + ledger,
                recipe.selectedHeroContentId, recipe.equippedArcanaContentId);
        }

        [Serializable] internal sealed class Recipe
        {
            public int schemaVersion;
            public string contentId;
            public List<IncomeSource> incomeSources;
            public List<string> purchases;
            public string progressPreset, selectedHeroContentId, equippedArcanaContentId, sourceDeclaration;
            public int expectedTotalIncome, expectedTotalSpent, expectedRemainingJade;
        }
        [Serializable] internal sealed class IncomeSource
        {
            public string evidencePath;
            public string sha256;
            public int revision;
            public int earnedJade;
        }
    }
}
