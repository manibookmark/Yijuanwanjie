using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns the lifetime of one isolated, explicitly declared campaign account and its quiescent
    /// Hub commands. Domain services alone grant rewards, debit purchases and
    /// admit Runs. The active Run borrows this same account; its host must expose
    /// these commands only while no Run is active. No user slot or GameSession is
    /// consulted. Fresh construction never replays progress; the named G4 slice
    /// alone obtains its declared permanent facts from ProgressAccountBlueprints.
    /// </summary>
    internal sealed class AgentGameCampaignAccount
    {
        private readonly List<RunEntryLoadoutProfile> _entries;
        private readonly Func<string, LevelRunProfile> _resolveFloor;
        private readonly List<CrawlerData> _heroes;
        private readonly List<ArcanaConfig> _arcanas;
        private readonly IProgressionRegistrySource _registry;
        private readonly Func<RunEntryLoadoutProfile, MetaProgression, string, string> _preflight;
        private readonly Dictionary<string, (string stamp, string error)> _entryChecks =
            new Dictionary<string, (string stamp, string error)>(StringComparer.Ordinal);

        internal MetaProgression Account { get; }
        internal string SelectedHeroId => Account.equippedCrawlers?.FirstOrDefault() ?? string.Empty;
        internal string InitialRecipeDeclaration { get; private set; } = string.Empty;
        internal string InitialRecipeFingerprint { get; private set; } = string.Empty;

        internal AgentGameCampaignAccount() : this(null) { }

        private AgentGameCampaignAccount(MetaProgression account) : this(
            Resources.LoadAll<RunEntryLoadoutProfile>("Content/RunEntries"),
            LevelRunProfileCatalog.Get,
            ContentCatalog.Current.Volumes.SelectMany(volume => volume.crawlers),
            Resources.LoadAll<ArcanaConfig>("Content/Arcanas"), LoadRegistry(), account: account) { }

        internal static AgentGameCampaignAccount G3ClearedForG4()
        {
            MetaProgression account = ProgressAccountBlueprints.Build(TestAccountPreset.G3Cleared,
                GameSession.DefaultCrawlerContentId);
            var owner = new AgentGameCampaignAccount(account);
            owner.ConfirmHero("crawler.xiyou.zhubajie");
            return owner;
        }

        internal static AgentGameCampaignAccount G3HistoricalForG4()
        {
            AgentGameProgressionAccount history = AgentGameProgressionAccount.LoadG4();
            var owner = new AgentGameCampaignAccount(history.Account) {
                InitialRecipeDeclaration = history.Declaration,
                InitialRecipeFingerprint = history.FingerprintPayload
            };
            owner.ConfirmHero(history.SelectedHeroContentId);
            owner.EquipArcana(history.EquippedArcanaContentId);
            return owner;
        }

        /// <summary>Loaded authoring seam; null account means genuinely fresh.
        /// A supplied isolated account is borrowed by subsequent Runs, never cloned or replayed again.</summary>
        internal AgentGameCampaignAccount(IEnumerable<RunEntryLoadoutProfile> entries,
            Func<string, LevelRunProfile> resolveFloor, IEnumerable<CrawlerData> heroes,
            IEnumerable<ArcanaConfig> arcanas, IProgressionRegistrySource registry,
            Func<RunEntryLoadoutProfile, MetaProgression, string, string> preflight = null,
            MetaProgression account = null)
        {
            Account = account ?? new MetaProgression();
            _entries = (entries ?? throw new ArgumentNullException(nameof(entries))).ToList();
            _resolveFloor = resolveFloor ?? throw new ArgumentNullException(nameof(resolveFloor));
            _heroes = (heroes ?? throw new ArgumentNullException(nameof(heroes)))
                .Where(hero => hero != null && !string.IsNullOrWhiteSpace(hero.contentId))
                .OrderBy(hero => hero.contentId, StringComparer.Ordinal).ToList();
            _arcanas = (arcanas ?? throw new ArgumentNullException(nameof(arcanas))).ToList();
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _preflight = preflight ?? FormalEntryPreflight;
            RequireUnique(_entries.Select(entry => entry?.contentId), "Run entry");
            RequireUnique(_heroes.Select(hero => hero.contentId), "crawler");
            RequireUnique(_arcanas.Select(arcana => arcana?.contentId), "arcana");
            _entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.contentId, right.contentId));
            _arcanas.Sort((left, right) => StringComparer.Ordinal.Compare(left.contentId, right.contentId));
        }

        internal static string FingerprintPayload
        {
            get
            {
                var text = new StringBuilder();
                ScriptableObject registry = Resources.Load<ScriptableObject>("Content/Hub/tianlu-registry-catalog");
                if (!(registry is IProgressionRegistrySource))
                    throw new InvalidOperationException("Tianlu catalog lacks its permanent-progress contract.");
                text.Append(JsonUtility.ToJson(registry));
                foreach (RunEntryLoadoutProfile entry in Resources.LoadAll<RunEntryLoadoutProfile>("Content/RunEntries")
                    .OrderBy(item => item.contentId, StringComparer.Ordinal)) text.Append(JsonUtility.ToJson(entry));
                foreach (ArcanaConfig arcana in Resources.LoadAll<ArcanaConfig>("Content/Arcanas")
                    .OrderBy(item => item.contentId, StringComparer.Ordinal)) text.Append(JsonUtility.ToJson(arcana));
                return text.ToString();
            }
        }

        /// <summary>Confirm the tutorial's actual granted hero; this command never grants or replays anything.</summary>
        internal void AcceptTutorialSelection()
        {
            if (!Account.IsLevelCleared(TutorialProgressionGate.LevelContentId) ||
                !Account.IsCrawlerUnlocked(TutorialProgressionGate.WukongCrawlerContentId))
                throw new InvalidOperationException("The tutorial must really settle and grant Wukong before confirming him.");
            if (string.IsNullOrEmpty(SelectedHeroId))
                Account.equippedCrawlers = new List<string> { TutorialProgressionGate.WukongCrawlerContentId };
        }

        internal void ConfirmHero(string contentId)
        {
            CrawlerData hero = _heroes.FirstOrDefault(item => item.contentId == contentId);
            if (!Account.IsG1FacilityStageUnlocked || hero == null ||
                !Account.IsCrawlerUnlocked(contentId) || !DemoReleasePolicy.AllowsCrawler(contentId))
                throw new InvalidOperationException("The Hall or selected crawler is not available.");
            // Match the Hall's primary-slot confirmation: selecting an existing
            // reserve swaps that slot with the old primary; a new primary leaves
            // every other slot in place.
            int capacity = Mathf.Clamp(1 + DaozangProgressionRules.GetLevel(Account, "crawler-slot"), 1, 3);
            var selected = new List<string>((Account.equippedCrawlers ?? new List<string>()).Take(capacity));
            while (selected.Count < capacity) selected.Add(string.Empty);
            int existing = selected.IndexOf(contentId);
            if (existing >= 0) selected[existing] = selected[0];
            selected[0] = contentId;
            Account.equippedCrawlers = selected;
        }

        internal void BuyUpgrade(string contentId)
        {
            AgentGameCampaignUpgrade upgrade = ReadUpgrade(DaozangProgressionRules.Get(contentId));
            if (!upgrade.purchasable) throw new InvalidOperationException(upgrade.blockedReason);
            PowerUpConfig definition = DaozangProgressionRules.CreatePowerUpConfig(contentId);
            try
            {
                if (!Account.BuyPowerUp(definition))
                    throw new InvalidOperationException("The formal Daozang purchase was rejected.");
            }
            finally { Release(definition); }
        }

        internal void EquipArcana(string contentId)
        {
            if (!Account.IsArcanaFacilityUnlocked)
                throw new InvalidOperationException("Arcana selection has not been unlocked.");
            if (string.IsNullOrEmpty(contentId))
            {
                Account.equippedArcana = null;
                Account.hasEquippedArcanaEffect = false;
                Account.equippedArcanaEffect = ArcanaEffect.Custom;
                return;
            }
            ArcanaConfig config = _arcanas.FirstOrDefault(item => item.contentId == contentId);
            if (config == null || !Account.EquipArcana(config))
                throw new InvalidOperationException("The selected published Arcana is not unlocked.");
        }

        /// <summary>Built only at the Hub. No automatic choice or implicit purchase is made.</summary>
        internal List<AgentGameCommand> Commands(Action<RunEntryLoadoutProfile> startEntry)
        {
            if (startEntry == null) throw new ArgumentNullException(nameof(startEntry));
            var result = new List<AgentGameCommand>();
            if (Account.IsG1FacilityStageUnlocked)
                foreach (CrawlerData hero in _heroes.Where(hero => Account.IsCrawlerUnlocked(hero.contentId) &&
                    DemoReleasePolicy.AllowsCrawler(hero.contentId) && hero.contentId != SelectedHeroId))
                {
                    string id = hero.contentId;
                    result.Add(Command("hub/hero/" + id, "SelectHero", id, "确认出战：" + hero.name,
                        "仅改变下一次新 Run 的领卷英灵。", () => ConfirmHero(id)));
                }
            foreach (DaozangProgressionRules.Definition definition in DaozangProgressionRules.All)
            {
                AgentGameCampaignUpgrade upgrade = ReadUpgrade(definition);
                if (!upgrade.purchasable) continue;
                string id = definition.Id;
                result.Add(Command("hub/upgrade/" + id, "BuyUpgrade", id,
                    definition.Name + " Lv" + (upgrade.level + 1),
                    "花费 " + upgrade.nextLevelPrice + " 灵玉。", () => BuyUpgrade(id)));
            }
            if (Account.IsArcanaFacilityUnlocked)
            {
                foreach (ArcanaConfig arcana in _arcanas.Where(item => Account.IsArcanaUnlocked(item.contentId) &&
                    item.contentId != Account.equippedArcana))
                {
                    string id = arcana.contentId;
                    result.Add(Command("hub/arcana/" + id, "EquipArcana", id,
                        "携带天机：" + arcana.arcanaName, arcana.description, () => EquipArcana(id)));
                }
                if (!string.IsNullOrEmpty(Account.equippedArcana))
                    result.Add(Command("hub/arcana/none", "EquipArcana", string.Empty,
                        "不携带天机", "清除下一次入卷所携带的天机。", () => EquipArcana(null)));
            }
            string stamp = EntryPermissionStamp();
            foreach (RunEntryLoadoutProfile entry in _entries)
            {
                AgentGameCampaignEntry view = ReadEntry(entry);
                if (!view.available) continue;
                string error = _preflight(entry, Account, SelectedHeroId);
                _entryChecks[entry.contentId] = (stamp, error);
                if (!string.IsNullOrEmpty(error)) continue;
                RunEntryLoadoutProfile chosen = entry;
                result.Add(Command("hub/entry/" + entry.contentId, "StartDungeon", entry.contentId,
                    "进入 " + entry.firstFloorContentId, "正式新 Run；应用当前英灵、经诀与天机。", () => startEntry(chosen)));
            }
            return result;
        }

        internal AgentGameCampaignAccountSnapshot Snapshot()
        {
            var value = new AgentGameCampaignAccountSnapshot {
                selectedHeroContentId = SelectedHeroId, equippedArcanaContentId = Account.equippedArcana,
                equippedArcanaEffect = Account.hasEquippedArcanaEffect ? Account.equippedArcanaEffect.ToString() : string.Empty,
                jade = Account.lingyu, totalJadeEarned = Account.totalLingyuEarned,
                highestPlayerLevel = Account.CardUnlocks.HighestPlayerLevel,
                playerExperienceGathered = Account.ExperienceCollections.LifetimeTotals.PlayerXPGathered,
                experienceCrystalPickups = Account.ExperienceCollections.LifetimeTotals.PickupCrystal,
                tutorialCompleted = Account.IsLevelCleared(TutorialProgressionGate.LevelContentId),
                daoZangUnlocked = Account.IsDaozangUnlocked, yinglingHallUnlocked = Account.IsG1FacilityStageUnlocked,
                wanbaogeUnlocked = Account.IsWanbaogeUnlocked, tianjiBuildingUnlocked = Account.IsTianjiBuildingUnlocked,
                arcanaSelectionUnlocked = Account.IsArcanaFacilityUnlocked, blacksmithUnlocked = Account.blacksmithUnlocked,
                chickenShopUnlocked = Account.EventUnlocks.ChickenShopUnlocked,
                manaOfferingUnlocked = Account.EventUnlocks.ManaOfferingUnlocked,
                duplicateOfferingUnlocked = Account.EventUnlocks.DuplicateOfferingUnlocked,
                evolutionOfferingUnlocked = Account.EventUnlocks.EvoOfferingUnlocked,
                gunForHireUnlocked = Account.EventUnlocks.GunForHireUnlocked,
                clearedLevelContentIds = Copy(Account.clearedLevelContentIds),
                unlockedCrawlerContentIds = Copy(Account.unlockedCrawlers), unlockedRelicContentIds = Copy(Account.unlockedRelics),
                unlockedArcanaContentIds = Copy(Account.unlockedArcanas), unlockedCardContentIds = Copy(Account.unlockedCards),
                unlockedSealContentIds = Copy(Account.unlockedSeals), equippedCrawlerContentIds = new List<string>(Account.equippedCrawlers ?? new List<string>()),
                powerUps = DaozangProgressionRules.All.Select(ReadUpgrade).ToList(),
                entries = _entries.Select(ReadEntry).ToList()
            };
            foreach (CrawlerData hero in _heroes.Where(item => !string.IsNullOrEmpty(item.cardContentId) || Account.IsCrawlerUnlocked(item.contentId)))
                value.heroes.Add(new AgentGameCampaignHero { contentId = hero.contentId, name = hero.name,
                    unlocked = Account.IsCrawlerUnlocked(hero.contentId),
                    selectable = Account.IsG1FacilityStageUnlocked && Account.IsCrawlerUnlocked(hero.contentId) && DemoReleasePolicy.AllowsCrawler(hero.contentId),
                    highestPlayerLevel = Account.CardUnlocks.GetHighestPlayerLevel(hero.contentId) });
            foreach (ArcanaConfig arcana in _arcanas)
                value.arcanas.Add(new AgentGameCampaignArcana { contentId = arcana.contentId,
                    name = arcana.arcanaName, description = arcana.description, effect = arcana.effect.ToString(),
                    unlocked = Account.IsArcanaUnlocked(arcana.contentId),
                    selectable = Account.IsArcanaFacilityUnlocked && Account.IsArcanaUnlocked(arcana.contentId),
                    equipped = Account.equippedArcana == arcana.contentId });
            IReadOnlyList<ProgressionRegistryItemSnapshot> registry = _registry.ReadProgression(Account)
                ?? throw new InvalidOperationException("Tianlu source returned no permanent-progress projection.");
            RequireUnique(registry.Select(entry => entry?.contentId), "Tianlu entry");
            foreach (ProgressionRegistryItemSnapshot entry in registry)
                value.tianlu.Add(new ProgressionRegistryItemSnapshot { contentId = entry.contentId,
                    kind = entry.kind, displayName = entry.displayName, unlocked = entry.unlocked });
            value.tianluTotalCount = value.tianlu.Count;
            value.tianluUnlockedCount = value.tianlu.Count(entry => entry.unlocked);
            foreach (CardUnlockCount fact in Account.CardUnlocks.LifetimeFacts.Counts)
                value.cardUnlockFacts.Add(new AgentGameCampaignCardUnlockFact {
                    metric = fact.Metric.ToString(), sourceContentId = fact.SourceContentId, count = fact.Count });
            return value;
        }

        internal string CaptureRecoveryDigest()
        {
            var text = new StringBuilder();
            void Add(string value) => text.Append(value?.Length ?? 0).Append(':').Append(value).Append('\n');
            Add(JsonUtility.ToJson(Account));
            Add(JsonUtility.ToJson(CardUnlockProgressSaveData.Capture(Account.CardUnlocks.Capture())));
            Add(JsonUtility.ToJson(EventUnlockProgressSaveData.Capture(Account.EventUnlocks.Capture())));
            Add(Account.ExperienceCollections.Serialize());
            foreach (var upgrade in Account.powerUpLevels.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            { Add(upgrade.Key); Add(upgrade.Value.ToString(CultureInfo.InvariantCulture)); }
            return AgentGameTrace.Hash(text.ToString());
        }

        private AgentGameCampaignUpgrade ReadUpgrade(DaozangProgressionRules.Definition definition)
        {
            int level = DaozangProgressionRules.GetLevel(Account, definition.Id);
            bool unlocked = !DaozangProgressionRules.IsUpgradeLocked(Account, definition.Id);
            int price = level < definition.MaxLevel ? DaozangProgressionRules.GetLevelPrice(definition.Id, level + 1) : 0;
            bool affordable = level < definition.MaxLevel && Account.lingyu >= price;
            string reason = !unlocked ? DaozangProgressionRules.GetUnlockHint(Account, definition.Tier)
                : level >= definition.MaxLevel ? "已满级。" : !affordable ? "灵玉不足。" : null;
            return new AgentGameCampaignUpgrade { contentId = definition.Id, name = definition.Name,
                tier = definition.Tier.ToString(), level = level, maxLevel = definition.MaxLevel,
                nextLevelPrice = price, unlocked = unlocked, affordable = affordable,
                purchasable = unlocked && affordable && level < definition.MaxLevel, blockedReason = reason };
        }

        private AgentGameCampaignEntry ReadEntry(RunEntryLoadoutProfile entry)
        {
            LevelRunProfile floor = _resolveFloor(entry.firstFloorContentId);
            bool unlocked = string.IsNullOrEmpty(floor.prerequisiteLevelContentId) || Account.IsLevelCleared(floor.prerequisiteLevelContentId);
            bool hero = !string.IsNullOrEmpty(SelectedHeroId) && Account.IsCrawlerUnlocked(SelectedHeroId) && DemoReleasePolicy.AllowsCrawler(SelectedHeroId);
            bool scope = DemoReleasePolicy.AllowsLevel(floor.contentId, floor.dungeonContentId);
            var value = new AgentGameCampaignEntry { contentId = entry.contentId, firstFloorContentId = floor.contentId,
                prerequisiteLevelContentId = floor.prerequisiteLevelContentId, unlocked = unlocked,
                available = unlocked && hero && scope,
                blockedReason = !unlocked ? "尚未通关前置整关：" + floor.prerequisiteLevelContentId
                    : !hero ? "尚未确认已解锁的出战英灵。" : !scope ? "当前版本不包含此正式入口。" : null };
            if (_entryChecks.TryGetValue(entry.contentId, out var check) && check.stamp == EntryPermissionStamp())
            {
                value.preflightChecked = true;
                value.available &= string.IsNullOrEmpty(check.error);
                if (!string.IsNullOrEmpty(check.error)) value.blockedReason = check.error;
            }
            return value;
        }

        private string EntryPermissionStamp() => CaptureRecoveryDigest();

        private static string FormalEntryPreflight(RunEntryLoadoutProfile entry, MetaProgression account, string hero)
        {
            if (RunStartService.TryPrepare(entry.contentId, account, hero, DaozangProgressionRules.ApplyToPlayerStats,
                    out _, out string error)) return null;
            return string.IsNullOrEmpty(error) ? "正式新 Run 预检拒绝入场。" : error;
        }

        private static IProgressionRegistrySource LoadRegistry()
            => Resources.Load<ScriptableObject>("Content/Hub/tianlu-registry-catalog") as IProgressionRegistrySource
                ?? throw new InvalidOperationException("Tianlu catalog lacks its permanent-progress contract.");

        private static AgentGameCommand Command(string actionId, string kind, string contentId,
            string title, string description, Action execute)
            => new AgentGameCommand(new AgentGameAction { actionId = actionId, kind = kind,
                contentId = contentId, title = title, description = description }, execute);

        private static List<string> Copy(IEnumerable<string> values)
            => (values ?? Array.Empty<string>()).OrderBy(value => value, StringComparer.Ordinal).ToList();

        private static void RequireUnique(IEnumerable<string> values, string kind)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in values)
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                    throw new InvalidOperationException("Campaign authoring contains a missing or duplicate " + kind + " ID.");
        }

        private static void Release(UnityEngine.Object value)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
