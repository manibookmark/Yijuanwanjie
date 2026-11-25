using System;
using System.Collections.Generic;
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Detached public account facts; no active deck, future RNG or save-slot state.</summary>
    [Serializable]
    public sealed class AgentGameCampaignAccountSnapshot
    {
        public string selectedHeroContentId, equippedArcanaContentId, equippedArcanaEffect;
        public int jade, totalJadeEarned, highestPlayerLevel, tianluUnlockedCount, tianluTotalCount;
        public long playerExperienceGathered, experienceCrystalPickups;
        public bool tutorialCompleted, daoZangUnlocked, yinglingHallUnlocked,
            wanbaogeUnlocked, tianjiBuildingUnlocked, arcanaSelectionUnlocked, blacksmithUnlocked;
        public bool chickenShopUnlocked, manaOfferingUnlocked, duplicateOfferingUnlocked,
            evolutionOfferingUnlocked, gunForHireUnlocked;
        public List<string> clearedLevelContentIds = new List<string>();
        public List<string> unlockedCrawlerContentIds = new List<string>();
        public List<string> unlockedRelicContentIds = new List<string>();
        public List<string> unlockedArcanaContentIds = new List<string>();
        public List<string> unlockedCardContentIds = new List<string>();
        public List<string> unlockedSealContentIds = new List<string>();
        public List<string> equippedCrawlerContentIds = new List<string>();
        public List<AgentGameCampaignHero> heroes = new List<AgentGameCampaignHero>();
        public List<AgentGameCampaignUpgrade> powerUps = new List<AgentGameCampaignUpgrade>();
        public List<AgentGameCampaignEntry> entries = new List<AgentGameCampaignEntry>();
        public List<AgentGameCampaignArcana> arcanas = new List<AgentGameCampaignArcana>();
        public List<ProgressionRegistryItemSnapshot> tianlu = new List<ProgressionRegistryItemSnapshot>();
        public List<AgentGameCampaignCardUnlockFact> cardUnlockFacts = new List<AgentGameCampaignCardUnlockFact>();
    }

    [Serializable] public sealed class AgentGameCampaignHero
    { public string contentId, name; public bool unlocked, selectable; public int highestPlayerLevel; }

    [Serializable] public sealed class AgentGameCampaignUpgrade
    {
        public string contentId, name, tier, blockedReason;
        public int level, maxLevel, nextLevelPrice;
        public bool unlocked, affordable, purchasable;
    }

    [Serializable] public sealed class AgentGameCampaignEntry
    {
        public string contentId, firstFloorContentId, prerequisiteLevelContentId, blockedReason;
        public bool unlocked, available, preflightChecked;
    }

    [Serializable] public sealed class AgentGameCampaignArcana
    { public string contentId, name, description, effect; public bool unlocked, selectable, equipped; }

    [Serializable] public sealed class AgentGameCampaignCardUnlockFact
    { public string metric, sourceContentId; public int count; }
}
