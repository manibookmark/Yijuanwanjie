using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Meta
{
    /// <summary>
    /// Owns the authored Daozang upgrade definitions and their account-to-player
    /// projection. UI and headless hosts share this catalog; MetaProgression
    /// remains the sole owner of purchases, balances and saved upgrade levels.
    /// Definitions contain no department layout, sprite paths or UI objects.
    /// </summary>
    public static class DaozangProgressionRules
    {
        public const string GreedUnlockLevelContentId = "level.xiyou.liushahe.f01";

        public readonly struct Definition
        {
            public readonly string Id, Name;
            public readonly int MaxLevel, PricePerLevel, AmountPerLevel;
            public readonly EconomyPriceTier PriceTier;
            public readonly UnlockTier Tier;
            public readonly StatType StatType;

            internal Definition(string id, string name, int maxLevel, int legacyPrice,
                int amountPerLevel, UnlockTier tier, StatType statType = StatType.None)
            {
                Id = id;
                Name = name;
                MaxLevel = maxLevel;
                // Retained for the existing UI value contract. Actual purchase
                // prices resolve the shared economy tier, not this historical price.
                PricePerLevel = legacyPrice;
                PriceTier = ResolvePriceTier(legacyPrice);
                AmountPerLevel = amountPerLevel;
                Tier = tier;
                StatType = statType;
            }
        }

        private static readonly IReadOnlyList<Definition> Definitions = Array.AsReadOnly(new[]
        {
            new Definition("might", "锋锐诀", 5, 60, 25, UnlockTier.Tier1, StatType.FengRui),
            new Definition("area", "裂空诀", 5, 50, 10, UnlockTier.Tier1, StatType.BengLie),
            new Definition("amount", "数量诀", 3, 80, 1, UnlockTier.Tier2, StatType.Amount),
            new Definition("reroll", "易卷诀", 5, 40, 2, UnlockTier.Tier1),
            new Definition("skip", "略卷诀", 5, 40, 2, UnlockTier.Tier2),
            new Definition("banish", "封卷诀", 5, 50, 2, UnlockTier.Tier3),
            new Definition("hand", "展卷诀", 2, 70, 1, UnlockTier.Tier3, StatType.ShouPai),
            new Definition("magnet", "引卷诀", 1, 120, 1, UnlockTier.Tier3),
            new Definition("duration", "驻灵诀", 5, 45, 1, UnlockTier.Tier1, StatType.NingShen),
            new Definition("crawler-slot", "御灵诀", 2, 90, 1, UnlockTier.Tier2),
            new Definition("mana", "聚灵诀", 2, 75, 1, UnlockTier.Tier2, StatType.FaLi),
            new Definition("growth", "明悟诀", 5, 35, 20, UnlockTier.Tier1, StatType.MingWu),
            new Definition("max-health", "固本诀", 5, 35, 10, UnlockTier.Tier1),
            new Definition("recovery", "调息诀", 3, 30, 1, UnlockTier.Tier1),
            new Definition("armor", "罡气诀", 3, 45, 2, UnlockTier.Tier1, StatType.HuJia),
            new Definition("revival", "还魂诀", 1, 150, 1, UnlockTier.Tier3, StatType.HunHun),
            new Definition("curse", "招煞诀", 5, 40, 20, UnlockTier.Tier3, StatType.ZhaoSha),
            new Definition("luck", "天运诀", 3, 55, 25, UnlockTier.Tier1, StatType.TianYun),
            new Definition("greed", "纳玉诀", 4, 65, 25, UnlockTier.Special, StatType.LianYu)
        });

        public static IReadOnlyList<Definition> All => Definitions;

        public static Definition Get(string id)
        {
            foreach (Definition definition in Definitions)
                if (definition.Id == id) return definition;
            throw new ArgumentException("Unknown Daozang upgrade contentId: " + id, nameof(id));
        }

        /// <summary>Read legacy display-name keys without writing or migrating the account.</summary>
        public static int GetLevel(MetaProgression account, string idOrLegacyName)
        {
            if (account?.powerUpLevels == null || string.IsNullOrWhiteSpace(idOrLegacyName)) return 0;
            foreach (Definition definition in Definitions)
            {
                if (definition.Id != idOrLegacyName && definition.Name != idOrLegacyName) continue;
                if (account.powerUpLevels.TryGetValue(definition.Id, out int stableLevel)) return stableLevel;
                if (account.powerUpLevels.TryGetValue(definition.Name, out int legacyLevel)) return legacyLevel;
                return 0;
            }
            return account.powerUpLevels.TryGetValue(idOrLegacyName, out int directLevel) ? directLevel : 0;
        }

        public static int GetLevelPrice(string id, int nextLevel)
        {
            Definition definition = Get(id);
            if (definition.Id == "greed")
            {
                int[] greedPrices = { 500, 750, 1000, 1500 };
                return greedPrices[Mathf.Clamp(nextLevel - 1, 0, greedPrices.Length - 1)];
            }
            return new EconomyConfig().GetPrice(definition.PriceTier);
        }

        public static bool IsLocked(MetaProgression account, UnlockTier tier)
        {
            if (account == null || !account.IsDaozangUnlocked) return true;
            return tier switch
            {
                UnlockTier.Tier1 => false,
                // Bridge milestones have not been published. Old flags cannot
                // invent a production condition for either future group.
                UnlockTier.Tier2 => true,
                UnlockTier.Tier3 => true,
                UnlockTier.Special => !account.IsLevelCleared(GreedUnlockLevelContentId),
                _ => true
            };
        }

        /// <summary>Purchase qualification belongs to the stable definition, never caller-owned tier fields.</summary>
        public static bool IsUpgradeLocked(MetaProgression account, string contentId)
        {
            foreach (Definition definition in Definitions)
                if (string.Equals(definition.Id, contentId, StringComparison.Ordinal))
                    return IsLocked(account, definition.Tier);
            return true;
        }

        public static string GetUnlockHint(MetaProgression account, UnlockTier tier)
        {
            if (!IsLocked(account, tier)) return string.Empty;
            return tier switch
            {
                UnlockTier.Tier1 => "通关双叉岭后开放道藏。",
                UnlockTier.Tier2 => "后续第一桥关开放，当前版本尚未开放。",
                UnlockTier.Tier3 => "后续第二桥关开放，当前版本尚未开放。",
                UnlockTier.Special => account != null && account.IsDaozangUnlocked
                    ? "通关流沙河后开放。"
                    : "通关双叉岭开放道藏，通关流沙河后开放此经诀。",
                _ => "此经诀尚未开放。"
            };
        }

        /// <summary>Caller owns the temporary configuration; purchases still use MetaProgression.</summary>
        public static PowerUpConfig CreatePowerUpConfig(string id)
        {
            Definition definition = Get(id);
            var config = ScriptableObject.CreateInstance<PowerUpConfig>();
            config.contentId = definition.Id;
            config.powerUpName = definition.Name;
            config.maxLevel = definition.MaxLevel;
            config.totalPrice = GetLevelPrice(id, 1) * definition.MaxLevel;
            config.amountPerLevel = definition.AmountPerLevel;
            config.tier = definition.Tier;
            config.statType = definition.StatType;
            config.usePriceTier = id != "greed";
            config.priceTier = definition.PriceTier;
            return config;
        }

        /// <summary>
        /// Projects onto a fresh player using the existing production contract.
        /// Callers must not repeatedly project the same player. Upgrade levels do
        /// not enable reward gates or invent effects for unimplemented upgrades.
        /// </summary>
        public static void ApplyToPlayerStats(MetaProgression account, PlayerStats stats)
        {
            if (account == null || stats == null) return;
            foreach (Definition definition in Definitions)
            {
                int level = GetLevel(account, definition.Id);
                if (level <= 0) continue;
                int amount = definition.AmountPerLevel * level;
                switch (definition.Id)
                {
                    case "might": stats.fengRui += amount; break;
                    case "area": stats.bengLie += amount; break;
                    case "amount": stats.amount += amount; break;
                    case "duration": stats.ningShen += amount; break;
                    case "hand": stats.shouPai += amount; break;
                    case "growth": stats.mingWu += amount; break;
                    case "greed": stats.lianYu += amount; break;
                    case "luck": stats.tianYun += amount; break;
                    case "curse": stats.zhaoSha += amount; break;
                    case "revival": stats.hunHun += amount; break;
                    case "armor": stats.armor += amount; break;
                    case "mana": stats.faLi += amount; break;
                    case "recovery": stats.recovery += amount; break;
                    case "max-health":
                        stats.maxHealth = Mathf.FloorToInt(stats.maxHealth * (1f + amount / 100f));
                        stats.currentHealth = stats.maxHealth;
                        break;
                }
            }
        }

        private static EconomyPriceTier ResolvePriceTier(int legacyPrice)
        {
            if (legacyPrice <= 45) return EconomyPriceTier.Tier1;
            if (legacyPrice <= 75) return EconomyPriceTier.Tier2;
            if (legacyPrice <= 100) return EconomyPriceTier.Tier3;
            if (legacyPrice <= 150) return EconomyPriceTier.Tier4;
            return EconomyPriceTier.Tier5;
        }
    }
}
