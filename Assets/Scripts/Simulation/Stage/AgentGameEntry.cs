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
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Owns admission and fixed content evidence for an isolated text
    /// test Run. Production preserves declared diagnostic account slices;
    /// Campaign borrows the campaign owner's actual account and never replays
    /// unlocks. RunStartService owns formal loadout/admission, Dungeon owns Run
    /// state, and this factory neither adopts a global Run nor writes user saves.
    /// The in-memory seam exercises those same transactions without freezing
    /// production route order in tests.</summary>
    internal sealed class AgentGameEntry
    {
        internal string TestCaseId, ContentFingerprint, StartingAccount, LeaderCrawlerContentId;
        internal LevelRunProfile FirstFloor;
        internal IReadOnlyList<LevelRunProfile> Floors;
        internal MetaProgression Account;
        internal DungeonCompletionUnlockCatalog CompletionUnlocks;
        internal Action<DungeonRunSession> Initialize;
        internal Func<int, string, IInstanceIdGenerator, DungeonRunSession> PrepareRun;
        internal Func<string, LevelRunProfile> ResolveFloor;
        internal Func<DungeonRunSession, RunAcceptanceEconomy, IRandomSource, AgentGameInteractions> OpenInteractions;
        internal Func<DungeonRunSession, LevelNode, IRandomSource, IInstanceIdGenerator, CombatRuleSet, StageEncounterAssembly> CreateEncounter;
        internal Func<LevelNode, bool> RewardUtilitiesAllowed;

        private static readonly string[] CampaignEntryIds = {
            "run-entry.xiyou.shuangchaling.v1",
            "run-entry.xiyou.yingchoujian.v1",
            "run-entry.xiyou.gaolaozhuang.v1",
            "run-entry.xiyou.liushahe.v1"
        };

        /// <summary>Resolves one actual campaign entry. The tutorial is its
        /// existing authored level entrance, before any hero is unlocked; the
        /// four ordinary dungeons use their formal Hub RunEntry definitions.
        /// Account mutations belong to the ensuing real gameplay transactions.</summary>
        internal static AgentGameEntry Campaign(MetaProgression account,
            string selectedHeroId, string runEntryContentId)
            => Campaign(account, selectedHeroId, runEntryContentId, null);

        /// <summary>The campaign owner may pass its already captured content
        /// header. Its lifetime is the frozen session, never a process-wide cache.</summary>
        internal static AgentGameEntry Campaign(MetaProgression account,
            string selectedHeroId, string runEntryContentId, string fixedContentFingerprint)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            AgentGameHostEnvironment.RequireFormalHost();
            bool tutorial = string.Equals(runEntryContentId,
                RunCombatRules.TutorialLevelContentId, StringComparison.Ordinal);
            if (!tutorial && !CampaignEntryIds.Contains(runEntryContentId, StringComparer.Ordinal))
                throw new ArgumentException("Unsupported campaign entry.", nameof(runEntryContentId));
            RunEntryLoadoutProfile formalEntry = tutorial ? null
                : RunEntryLoadoutProfileCatalog.Get(runEntryContentId);
            LevelRunProfile first = LevelRunProfileCatalog.Get(tutorial
                ? RunCombatRules.TutorialLevelContentId : formalEntry.firstFloorContentId);
            IReadOnlyList<LevelRunProfile> floors = ResolveFloorChain(first);
            var entry = new AgentGameEntry {
                TestCaseId = AgentGameProtocol.CampaignTestCaseId,
                FirstFloor = first, Floors = floors, Account = account,
                LeaderCrawlerContentId = tutorial ? null : selectedHeroId,
                ContentFingerprint = fixedContentFingerprint ?? CampaignContentFingerprint(),
                StartingAccount = "Same isolated campaign account; actual gameplay inheritance; " +
                    (tutorial ? "authored tutorial entrance; no hero granted or selected by entry"
                        : "formal entry=" + formalEntry.contentId + "; selected hero=" + selectedHeroId) +
                    "; no blueprint/reward replay or user save imported",
                ResolveFloor = LevelRunProfileCatalog.Get,
                RewardUtilitiesAllowed = node => BattleScenarioCatalog.Get(node.scenarioId)
                    .rewardUtilityPolicy != RewardUtilityPolicy.Disabled,
                OpenInteractions = AgentGameInteractions.Open
            };
            // Formal scenes apply the account for each fresh battle. The Run
            // restores persistent stats; initial armor stays a per-battle value.
            entry.CreateEncounter = (run, node, random, identities, rules) => new StageEncounterAssembler().Create(run,
                BattleScenarioCatalog.Get(node.scenarioId), random, identities, rules,
                playerProjection: player => DaozangProgressionRules.ApplyToPlayerStats(account, player),
                rewardAccount: account, nodeContentId: node.contentId);
            entry.PrepareRun = (seed, runId, identities) => {
                if (tutorial) return PrepareTutorial(account, first, seed, runId, identities);
                List<CardConfig> pool = ContentRuntimeFactory.CreateCardPool(formalEntry.volumeId);
                try {
                    var service = new RunStartService(formalEntry, first, pool,
                        DaozangProgressionRules.ApplyToPlayerStats);
                    AgentGameInteractions.Require(service.TryPrepare(account, selectedHeroId,
                        out DungeonRunSession prepared, out string error, runId, identities, seed), error);
                    return prepared;
                } finally { AgentGameInteractions.DestroyCards(pool); }
            };
            return entry;
        }

        /// <summary>Static campaign-wide inputs only. Including all four formal
        /// chains and the tutorial gives every campaign step the same content
        /// header. This never creates a Run, actor, native curve or RNG sample.</summary>
        internal static string CampaignContentFingerprint()
        {
            var content = new StringBuilder();
            // Contracts are immutable during this synchronous capture. Reuse a
            // dataset's validated static inputs while retaining every node's
            // original append order and fields in the fingerprint payload.
            var sources = new Dictionary<string, CommonEncounterSourceContract>(StringComparer.Ordinal);
            var resolver = new CommonEncounterSourceResolver(ContentCatalog.Current);
            Append(content, "campaign-content.v1");
            TextAsset manifest = Resources.Load<TextAsset>("Content/Generated/content-manifest");
            if (manifest == null) throw new InvalidOperationException("Generated content manifest is missing.");
            Append(content, manifest.text);
            TextAsset statuses = Resources.Load<TextAsset>("card_test_status");
            if (statuses == null) throw new InvalidOperationException("Formal card acceptance registry is missing.");
            Append(content, statuses.text);
            AppendFloorChain(content, LevelRunProfileCatalog.Get(RunCombatRules.TutorialLevelContentId), resolver, sources);
            foreach (string id in CampaignEntryIds)
            {
                RunEntryLoadoutProfile profile = RunEntryLoadoutProfileCatalog.Get(id);
                Append(content, JsonUtility.ToJson(profile));
                if (!string.IsNullOrEmpty(profile.referenceLoadoutContentId))
                    Append(content, JsonUtility.ToJson(RunReferenceLoadoutProfileCatalog.Get(
                        profile.referenceLoadoutContentId)));
                AppendFloorChain(content, LevelRunProfileCatalog.Get(profile.firstFloorContentId), resolver, sources);
            }
            return AgentGameTrace.Hash(content.ToString());
        }

        private static DungeonRunSession PrepareTutorial(MetaProgression account,
            LevelRunProfile first, int seed, string runId, IInstanceIdGenerator identities)
        {
            List<string> errors = first.Validate();
            if (errors.Count != 0) throw new InvalidOperationException(string.Join(" | ", errors));
            // OpeningCG starts this exact authored entrance with a fresh Run.
            // No RunEntry/hero package exists before the tutorial's finale grant.
            DungeonRunSession run = DungeonRunSession.Begin(first,
                first.ResolveDefaultSnapshotId(), runId, identities, runSeed: seed,
                cardPlayGrowthEnabled: RunStartService.CaptureCardPlayGrowthAdmission(account),
                commonEncounterSources: id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id));
            BattleScenarioProfile scenario = RunAcceptanceDecisions.ResolveScenario(run.CurrentFloor);
            if (scenario.testOnlyUnconfirmed || scenario.HasReferenceLoadout)
                throw new InvalidOperationException("Tutorial requires its formal authored teaching loadout.");
            List<CardConfig> pool = ContentRuntimeFactory.CreateCardPool(scenario.volumeId);
            try {
                List<CardConfig> deck = RunCombatDeckResolver.Resolve(scenario, run,
                    null, pool, CardTestStatus.CanUseInFormalPlay);
                PlayerStats player = scenario.CreatePlayer();
                DaozangProgressionRules.ApplyToPlayerStats(account, player);
                AgentGameInteractions.Require(run.TryPrepareCombat(player, deck, pool,
                    out List<CardConfig> prepared, out string error), error);
                AgentGameInteractions.DestroyCards(prepared);
                return run;
            } finally { AgentGameInteractions.DestroyCards(pool); }
        }

        private static IReadOnlyList<LevelRunProfile> ResolveFloorChain(LevelRunProfile first)
        {
            var floors = new List<LevelRunProfile>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            LevelRunProfile floor = first;
            while (floor != null)
            {
                if (!visited.Add(floor.contentId)) throw new InvalidOperationException("Floor chain contains a cycle.");
                floors.Add(floor);
                floor = string.IsNullOrEmpty(floor.nextFloorContentId) ? null
                    : LevelRunProfileCatalog.Get(floor.nextFloorContentId);
            }
            return floors;
        }

        private static void AppendFloorChain(StringBuilder content, LevelRunProfile first,
            CommonEncounterSourceResolver resolver,
            Dictionary<string, CommonEncounterSourceContract> sources)
        {
            foreach (LevelRunProfile floor in ResolveFloorChain(first))
            {
                Append(content, JsonUtility.ToJson(floor));
                foreach (LevelNode node in floor.nodes.Where(n => n != null &&
                    (n.nodeType == LevelNodeType.Encounter || n.nodeType == LevelNodeType.Finale)))
                {
                    Append(content, JsonUtility.ToJson(BattleScenarioCatalog.Get(node.scenarioId)));
                    if (!node.HasCommonEncounter) continue;
                    LevelCommonEncounterBinding binding = node.commonEncounter;
                    CommonEncounterSourceContract source = ResolveSource(binding.datasetContentId, resolver, sources);
                    if (!source.Source.HasDifficulty(binding.difficultyContentId))
                        throw new InvalidOperationException("Unknown common encounter difficulty: " + binding.difficultyContentId);
                    foreach (string item in new[] { "common-encounter-source", node.contentId,
                        source.ContentId, source.GenerationFingerprint, source.RuntimeContentFingerprint,
                        binding.difficultyContentId, binding.hasTileDifficulty.ToString(),
                        BitConverter.ToInt32(BitConverter.GetBytes(binding.tileDifficulty), 0)
                            .ToString(CultureInfo.InvariantCulture) })
                        Append(content, item);
                }
            }
        }

        private static void Append(StringBuilder target, string value)
        { value ??= string.Empty; target.Append(value.Length).Append(':').Append(value).Append(';'); }

        private static CommonEncounterSourceContract ResolveSource(string datasetContentId,
            CommonEncounterSourceResolver resolver,
            Dictionary<string, CommonEncounterSourceContract> sources)
        {
            if (!sources.TryGetValue(datasetContentId, out CommonEncounterSourceContract source))
            {
                source = resolver.Resolve(datasetContentId);
                sources.Add(datasetContentId, source);
            }
            return source;
        }

        internal static AgentGameEntry Production(string testCaseId)
        {
            if (!AgentGameProtocol.IsSupportedTestCase(testCaseId))
                throw new ArgumentException("Unsupported testCaseId.");
            AgentGameHostEnvironment.RequireFormalHost();
            bool g2Reference = testCaseId == AgentGameProtocol.G2VcReferenceTestCaseId;
            bool g3Historical = testCaseId == AgentGameProtocol.G3HistoricalDaozangTestCaseId;
            bool g3Production = testCaseId == AgentGameProtocol.G3ProductionTestCaseId || g3Historical;
            bool g4Production = testCaseId == AgentGameProtocol.G4ProductionTestCaseId;
            bool formalProduction = g3Production || g4Production;
            RunReferenceLoadoutProfile reference = g2Reference
                ? RunReferenceLoadoutProfileCatalog.Get("loadout.xiyou.yingchoujian.f01.vc-reference.v1") : null;
            if (g2Reference && (reference == null || reference.ValidateProfile().Count != 0))
                throw new InvalidOperationException("G2 VC diagnostic requires its independent reference loadout.");
            AgentGameProgressionAccount historicalAccount = g3Historical ? AgentGameProgressionAccount.LoadG3() : null;
            const string wukong = "legacy.crawler.common.5a3f3db3";
            const string tangSanzang = "crawler.xiyou.tangsanzang.v1";
            const string bailongma = "crawler.xiyou.bailongma.v1";
            const string zhubajie = "crawler.xiyou.zhubajie";
            RunEntryLoadoutProfile formalEntry = formalProduction
                ? RunEntryLoadoutProfileCatalog.Get(g4Production
                    ? "run-entry.xiyou.liushahe.v1" : "run-entry.xiyou.gaolaozhuang.v1") : null;
            string hero = g4Production ? zhubajie : g3Production ? bailongma : g2Reference ? tangSanzang : wukong;
            LevelRunProfile first = LevelRunProfileCatalog.Get(formalProduction
                ? formalEntry.firstFloorContentId : g2Reference
                ? "level.xiyou.yingchoujian.v1"
                : "level.xiyou.shuangchaling.v1");
            var floors = new List<LevelRunProfile>(); var visited = new HashSet<string>();
            var sources = new Dictionary<string, CommonEncounterSourceContract>(StringComparer.Ordinal);
            var resolver = new CommonEncounterSourceResolver(ContentCatalog.Current);
            string cursor = first.contentId;
            var content = new System.Text.StringBuilder();
            TextAsset manifest = Resources.Load<TextAsset>("Content/Generated/content-manifest");
            if (manifest == null) throw new InvalidOperationException("Generated content manifest is missing.");
            content.Append(manifest.text);
            if (formalEntry != null) content.Append(JsonUtility.ToJson(formalEntry));
            if (reference != null) content.Append(JsonUtility.ToJson(reference));
            if (historicalAccount != null) content.Append(historicalAccount.FingerprintPayload);
            while (!string.IsNullOrEmpty(cursor))
            {
                if (!visited.Add(cursor)) throw new InvalidOperationException("Floor chain contains a cycle.");
                LevelRunProfile floor = LevelRunProfileCatalog.Get(cursor); floors.Add(floor); content.Append(JsonUtility.ToJson(floor));
                foreach (LevelNode node in floor.nodes.Where(n => n != null && (n.nodeType == LevelNodeType.Encounter || n.nodeType == LevelNodeType.Finale)))
                {
                    content.Append(JsonUtility.ToJson(BattleScenarioCatalog.Get(node.scenarioId)));
                    if (!node.HasCommonEncounter) continue;
                    LevelCommonEncounterBinding binding = node.commonEncounter;
                    CommonEncounterSourceContract source = ResolveSource(binding.datasetContentId, resolver, sources);
                    if (!source.Source.HasDifficulty(binding.difficultyContentId))
                        throw new InvalidOperationException("Unknown common encounter difficulty: " + binding.difficultyContentId);
                    // Static input only: no Run, native curve evaluation, actors or
                    // RNG are sampled while computing the trace's fixed header.
                    foreach (string item in new[] { "common-encounter-source", node.contentId,
                        source.ContentId, source.GenerationFingerprint, source.RuntimeContentFingerprint,
                        binding.difficultyContentId, binding.hasTileDifficulty.ToString(),
                        BitConverter.ToInt32(BitConverter.GetBytes(binding.tileDifficulty), 0)
                            .ToString(CultureInfo.InvariantCulture) })
                        content.Append(item.Length).Append(':').Append(item).Append(';');
                }
                cursor = floor.nextFloorContentId;
            }
            var entry = new AgentGameEntry { TestCaseId = testCaseId, FirstFloor = first, Floors = floors,
                LeaderCrawlerContentId = hero,
                ContentFingerprint = AgentGameTrace.Hash(content.ToString()),
                StartingAccount = g3Historical
                    ? "G2Cleared permanent-unlock blueprint; selected hero=" + hero +
                        "; formal entry=" + formalEntry.contentId + "; " + historicalAccount.Declaration
                    : formalProduction
                    ? (g4Production ? "G3Cleared" : "G2Cleared") + " permanent-unlock blueprint; selected hero=" + hero +
                        "; formal entry=" + formalEntry.contentId +
                        "; no upgrades/currency/equipped arcana; no prior temporary build or user save imported" +
                        (g4Production ? "; Shawujing remains locked until the G4 finale" : "")
                    : g2Reference
                    ? "G1Cleared; selected hero=" + hero + "; authored VC reference loadout; no user save imported"
                    : "PostTutorial; selected hero=" + hero + "; no upgrades/currency",
                Account = historicalAccount?.Account ?? ProgressAccountBlueprints.Build(
                    g4Production ? TestAccountPreset.G3Cleared : g3Production ? TestAccountPreset.G2Cleared : g2Reference ? TestAccountPreset.G1Cleared : TestAccountPreset.PostTutorial,
                    wukong), ResolveFloor = LevelRunProfileCatalog.Get,
                RewardUtilitiesAllowed = node => BattleScenarioCatalog.Get(node.scenarioId).rewardUtilityPolicy != RewardUtilityPolicy.Disabled,
                OpenInteractions = AgentGameInteractions.Open };
            entry.CreateEncounter = (run, node, random, identities, rules) => new StageEncounterAssembler().Create(run,
                BattleScenarioCatalog.Get(node.scenarioId), random, identities, rules,
                playerProjection: g3Historical
                    ? (Action<PlayerStats>)(player => DaozangProgressionRules.ApplyToPlayerStats(entry.Account, player))
                    : null,
                rewardAccount: entry.Account, nodeContentId: node.contentId);
            if (formalProduction)
            {
                entry.PrepareRun = (seed, runId, identities) => {
                    List<CardConfig> pool = ContentRuntimeFactory.CreateCardPool(formalEntry.volumeId);
                    try {
                        Action<MetaProgression, PlayerStats> projection = g3Historical
                            ? (Action<MetaProgression, PlayerStats>)DaozangProgressionRules.ApplyToPlayerStats : ApplyDeclaredNoUpgradeStats;
                        var service = new RunStartService(formalEntry, first, pool, projection);
                        AgentGameInteractions.Require(service.TryPrepare(entry.Account, hero,
                            out DungeonRunSession prepared, out string error, runId, identities, seed), error);
                        return prepared;
                    } finally { AgentGameInteractions.DestroyCards(pool); }
                };
                return entry;
            }
            entry.Initialize = run => {
                BattleScenarioProfile scenario = RunAcceptanceDecisions.ResolveScenario(run.CurrentFloor);
                if (scenario.testOnlyUnconfirmed || scenario.HasReferenceLoadout)
                    throw new InvalidOperationException("Entry does not match the declared test-case semantics.");
                List<CardConfig> pool = ContentRuntimeFactory.CreateCardPool(scenario.volumeId);
                try {
                    List<CardConfig> deck = g2Reference
                        ? reference.ResolveDeck(
                            pool, CardTestStatus.CanUseInFormalPlay)
                        : CrawlerStartingDeckResolver.ComposeRunEntryDeck(scenario.deckContentIds,
                            scenario.crawlerContentId, hero, pool, CardTestStatus.CanUseInFormalPlay);
                    PlayerStats player = scenario.CreatePlayer();
                    reference?.ApplyTo(player);
                    AgentGameInteractions.Require(run.TryPrepareCombat(player, deck, pool, out List<CardConfig> instances, out string error), error);
                    AgentGameInteractions.DestroyCards(instances);
                } finally { AgentGameInteractions.DestroyCards(pool); }
            };
            return entry;
        }

        // This test case declares an unupgraded account. Reject changes to that
        // declaration instead of silently omitting equipped/upgraded effects.
        private static void ApplyDeclaredNoUpgradeStats(MetaProgression account, PlayerStats player)
        {
            if (account == null || player == null || account.lingyu != 0 ||
                account.powerUpLevels == null || account.powerUpLevels.Any(pair => pair.Value != 0) ||
                !string.IsNullOrEmpty(account.equippedArcana) || account.hasEquippedArcanaEffect)
                throw new InvalidOperationException("Formal production-start test case requires its declared no-upgrades/no-equipped-arcana account.");
        }
    }
}
