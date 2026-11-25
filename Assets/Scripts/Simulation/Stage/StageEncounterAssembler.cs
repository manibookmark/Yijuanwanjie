using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Builds one headless CombatController from production floor, scenario, card,
    /// enemy, progression, and reward configuration. Callers do not need to know
    /// how those content sources are combined, and this module never owns Run state.
    /// </summary>
    internal sealed class StageEncounterAssembler
    {
        private readonly Func<BattleScenarioProfile, CombatRuleSet, CommonEncounterFrozenRoster, EncounterScenarioSpec> _createSpec;

        public StageEncounterAssembler() : this((profile, rules, roster) => ScenarioFactory.FromProfile(profile, rules, roster)) { }

        /// <summary>
        /// The factory validates its input, then transfers its freshly materialized card pool and bound
        /// enemy temporary-card definitions. This seam also permits synthetic
        /// lifetime contracts without loading production content or real saves.
        /// </summary>
        internal StageEncounterAssembler(Func<BattleScenarioProfile, CombatRuleSet, EncounterScenarioSpec> createSpec)
        {
            if (createSpec == null) throw new ArgumentNullException(nameof(createSpec));
            _createSpec = (profile, rules, roster) =>
            {
                if (roster != null)
                    throw new InvalidOperationException("The legacy factory cannot materialize a generated frozen roster.");
                return createSpec(profile, rules);
            };
        }

        internal StageEncounterAssembler(Func<BattleScenarioProfile, CombatRuleSet,
            CommonEncounterFrozenRoster, EncounterScenarioSpec> createSpec)
            => _createSpec = createSpec ?? throw new ArgumentNullException(nameof(createSpec));

        /// <summary>
        /// Captures this encounter's pre-turn production entry. Each planning
        /// invocation restores a separate Run, card-ID source and owned content;
        /// no branch shares the authoritative progression or starts a turn here.
        /// </summary>
        public Func<IRandomSource, CombatPlanningBranch> CaptureBranchFactory(
            DungeonRunSession dungeon, BattleScenarioProfile scenario,
            int nextInstanceId, CombatRuleSet rules = null,
            Action<PlayerStats> playerProjection = null,
            MetaProgression rewardAccount = null, string nodeContentId = null)
            => CaptureBranchFactory(dungeon.Serialize(), scenario, nextInstanceId, rules,
                playerProjection, rewardAccount, nodeContentId);

        internal Func<IRandomSource, CombatPlanningBranch> CaptureBranchFactory(
            string payload, BattleScenarioProfile scenario,
            int nextInstanceId, CombatRuleSet rules = null,
            Action<PlayerStats> playerProjection = null,
            MetaProgression rewardAccount = null, string nodeContentId = null)
        {
            CombatRuleSet frozenRules = rules?.Clone() ?? new CombatRuleSet();
            MetaProgression frozenAccount = FreezeRewardAccount(rewardAccount);
            return random =>
            {
                var identities = new SeededInstanceIdGenerator(nextInstanceId);
                if (!DungeonRunSession.TryRestore(payload, LevelRunProfileCatalog.Get,
                        identities, null, out DungeonRunSession copy, out string error,
                        commonEncounterSources: id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id)))
                    throw new InvalidOperationException("Cannot restore isolated planning entry: " + error);
                StageEncounterAssembly assembly = Create(copy, scenario, random, identities,
                    frozenRules, playerProjection, frozenAccount, nodeContentId);
                return new CombatPlanningBranch(assembly.Combat, assembly);
            };
        }

        public StageEncounterAssembly Create(
            DungeonRunSession dungeon,
            BattleScenarioProfile scenario,
            IRandomSource random,
            IInstanceIdGenerator instanceIds,
            CombatRuleSet rules = null,
            Action<PlayerStats> playerProjection = null,
            MetaProgression rewardAccount = null, string nodeContentId = null)
        {
            if (dungeon == null)
                throw new ArgumentNullException(nameof(dungeon));
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (instanceIds == null)
                throw new ArgumentNullException(nameof(instanceIds));
            dungeon.ConfigureSealRewardEligibility(() =>
                SealRewardEligibility.CanReceiveSeals(rewardAccount));

            // Read the authoritative frozen occurrence before materializing actors.
            // Active encounters validate their launch; isolated entries carry a node
            // explicitly. No query may guess a repeated Scenario or regenerate it.
            bool explicitLaunch = nodeContentId != null || dungeon.CurrentFloor.ActiveEncounter.HasValue;
            // Old standalone callers retain the prior unique authored/preset lookup.
            // Its formation guard must reject a generated mode without an exact node.
            DungeonRunEncounterSnapshot encounter = explicitLaunch
                ? dungeon.ResolveEnemyRoster(scenario.scenarioId, nodeContentId) : null;
            CommonEncounterFrozenRoster roster = encounter?.GeneratedRoster;
            EncounterScenarioSpec spec = _createSpec(
                scenario, rules ?? new CombatRuleSet(), roster);
            var assembly = new StageEncounterAssembly(spec);
            List<CardConfig> runDeck = null;
            bool completed = false;
            try
            {
                if (roster != null)
                {
                    if (spec.Enemies == null || spec.Enemies.Count != roster.EnemyContentIds.Count)
                        throw new InvalidOperationException("Materialized roster count does not match the frozen encounter.");
                    for (int i = 0; i < spec.Enemies.Count; i++)
                        if (spec.Enemies[i] == null || !string.Equals(spec.Enemies[i].contentId,
                                roster.EnemyContentIds[i], StringComparison.Ordinal))
                            throw new InvalidOperationException("Materialized actor order does not match the frozen encounter.");
                }
                EnemyFormationPlan formation = dungeon.ResolveEnemyFormation(scenario.scenarioId, spec.Enemies, encounter?.NodeContentId ?? nodeContentId);
                playerProjection?.Invoke(spec.Player);
                IReadOnlyList<CardConfig> cardPool = spec.OwnedCardPool;
                if (!dungeon.TryPrepareCombat(
                        spec.Player,
                        spec.Deck,
                        cardPool,
                        out runDeck,
                        out string prepareError))
                {
                    throw new InvalidOperationException(
                        $"Cannot prepare Run combat state: {prepareError}");
                }

                LevelRunModifierSnapshot modifiers =
                    dungeon.CurrentFloor.Modifiers;
                // Run bonuses can already be int.MaxValue; clamp after widening the sum.
                spec.Player.faLi = (int)Math.Max(1L,
                    Math.Min(int.MaxValue, (long)spec.Player.faLi + modifiers.BaseManaBonus));
                spec.Player.baseArmorPerTurn = (int)Math.Max(0L,
                    Math.Min(int.MaxValue, (long)spec.Player.baseArmorPerTurn + modifiers.BaseArmorPerTurnBonus));
                spec.Player.fengRui = (int)Math.Max(0L,
                    Math.Min(int.MaxValue, (long)spec.Player.fengRui + modifiers.MightBonus));
                spec.Player.bengLie = (int)Math.Max(0L,
                    Math.Min(int.MaxValue, (long)spec.Player.bengLie + modifiers.AreaBonus));
                spec.Player.shouPai = (int)Math.Max(1L,
                    Math.Min(int.MaxValue, (long)spec.Player.shouPai + modifiers.HandLimitBonus));

                LevelUpRewardRuleResolution rewardRule = ResolveRewardRule(
                    dungeon.CurrentFloor.Profile,
                    scenario,
                    cardPool,
                    rewardAccount,
                    new CardUnlockRunContext(dungeon.RunId,
                        () => dungeon.RunProgression.CardUnlockFacts.Capture(), dungeon.LeaderCrawlerContentId));
                // Match formal combat's lookup over this encounter's owned pool.
                // Definitions stay alive until the assembly releases its content.
                CardConfig ResolveCreatedCardDefinition(string contentId)
                {
                    if (string.IsNullOrWhiteSpace(contentId)) return null;
                    for (int i = 0; i < cardPool.Count; i++)
                    {
                        CardConfig candidate = cardPool[i];
                        if (candidate != null && string.Equals(candidate.contentId,
                                contentId, StringComparison.OrdinalIgnoreCase))
                            return candidate;
                    }
                    return null;
                }
                List<CardConfig> fixedRewards = ResolveCards(
                    scenario.fixedFirstLevelUpRewardCardContentIds,
                    cardPool,
                    scenario,
                    "fixed first level-up rewards");
                List<CardConfig> forcedOpeningHand = ResolveCards(
                    scenario.forcedOpeningHandContentIds,
                    cardPool,
                    scenario,
                    "forced opening hand");
                var guarantees = dungeon.PendingForcedOpeningHandCardInstanceIds;
                for (int guaranteedIndex = guarantees.Count - 1; guaranteedIndex >= 0; guaranteedIndex--)
                    AddForcedRunCard(forcedOpeningHand, runDeck, guarantees[guaranteedIndex]);

                BossTimer bossTimer = scenario.bossTimerEnabled
                    ? new BossTimer(scenario.bossTimerEyes, scenario.bossTimerDamage)
                    : null;
                var economy = new EconomySystem(
                    new EconomyConfig(),
                    dungeon.RunProgression);
                CrawlerConfig CreateTrackedCrawler(string contentId)
                {
                    CrawlerConfig crawler =
                        ContentRuntimeFactory.CreateCrawlerByContentId(contentId);
                    assembly.TrackOwnedObject(crawler);
                    return crawler;
                }

                var combat = new CombatController(
                    spec.Player,
                    runDeck,
                    new List<EnemyStats>(spec.Enemies),
                    bossTimer,
                    rewardRule.Cards,
                    spec.Rules,
                    dungeon.RunProgression,
                    economy,
                    formation == null ? spec.EnemyWaveSizes : null,
                    fixedRewards,
                    dungeon.CurrentFloor.CombatProgression,
                    formation == null ? spec.EnemyFormationRows : null,
                    formation == null ? spec.EnemyFormationPattern : null,
                    forcedOpeningHand.Count > 0 ? forcedOpeningHand : null,
                    CreateTrackedCrawler,
                    rewardRule.Config,
                    instanceIds,
                    dungeon.CrawlerResidents,
                    CardDeckInitialization.RestoreInstances,
                    levelUpOfferResolver: LevelUpOfferScheduleAdapter.ResolveForFloor(
                        dungeon.CurrentFloor.Profile,
                        cardPool,
                        contentId => IsRewardCardEligible(scenario, contentId),
                        canReceiveSeals: () => SealRewardEligibility.CanReceiveSeals(rewardAccount)),
                    isEvolutionResultEligible: contentId =>
                        scenario.testOnlyUnconfirmed
                            ? CardTestStatus.CanUseInTestBattle(contentId)
                            : CardTestStatus.CanUseEvolutionResult(contentId),
                    rewardRerollBank: dungeon,
                    naturalRewardPool: rewardRule.NaturalPool,
                    enemyFormationPlan: formation,
                    cardResolver: ResolveCreatedCardDefinition,
                    canReceiveSeals: () => SealRewardEligibility.CanReceiveSeals(rewardAccount));
                assembly.AttachCombat(combat);
                combat.SetRandomSource(random);
                completed = true;
                return assembly;
            }
            finally
            {
                // TryPrepareCombat can return false (or throw) with a partial
                // out deck. The same owner handles that acquisition and every
                // later failure, including reward resolution and combat setup.
                assembly.TrackCards(runDeck);
                if (!completed) assembly.Dispose();
            }
        }

        internal static LevelUpRewardRuleResolution ResolveRewardRule(
            LevelRunProfile floor,
            BattleScenarioProfile scenario,
            IReadOnlyList<CardConfig> fullCardPool,
            MetaProgression rewardAccount = null,
            CardUnlockRunContext runContext = null)
        {
            List<string> errors = GlobalLevelUpRewardRuleProfileCatalog.ValidateLevelRuleReference(
                floor.levelUpRewardRuleProfileId);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            return NaturalLevelUpRewardCatalog.ResolveGlobal(
                fullCardPool,
                scenario.volumeId,
                rewardAccount ?? new MetaProgression(),
                contentId => IsRewardCardEligible(
                    scenario,
                    contentId),
                runContext: runContext);
        }

        // Only reward eligibility crosses the planning/replay boundary, never
        // the mutable wallet, progression dictionaries or the user's save.
        internal static MetaProgression FreezeRewardAccount(MetaProgression account)
        {
            if (account == null) return null;
            var frozen = new MetaProgression
            {
                unlockedCards = new List<string>(account.unlockedCards ?? new List<string>()),
                unlockedSeals = new List<string>(account.unlockedSeals ?? new List<string>()),
                unlockedRelics = new List<string>(account.unlockedRelics ?? new List<string>()),
                unlockedRelicTypes = new List<RelicType>(account.unlockedRelicTypes ?? new List<RelicType>()),
                unlockedCrawlers = new List<string>(account.unlockedCrawlers ?? new List<string>())
            };
            frozen.CardUnlocks.Restore(account.CardUnlocks.Capture());
            return frozen;
        }

        private static List<CardConfig> ResolveCards(
            IReadOnlyList<string> contentIds,
            IReadOnlyList<CardConfig> cardPool,
            BattleScenarioProfile scenario,
            string label)
        {
            if (contentIds == null || contentIds.Count == 0)
                return new List<CardConfig>();
            return StableCardPoolResolver.ResolveEligible(
                contentIds,
                cardPool,
                contentId => IsCardEligible(scenario, contentId),
                label);
        }

        private static bool IsCardEligible(
            BattleScenarioProfile scenario,
            string contentId)
        {
            return scenario.testOnlyUnconfirmed
                ? CardTestStatus.CanUseInTestBattle(contentId)
                : CardTestStatus.CanUseInFormalPlay(contentId);
        }

        private static bool IsRewardCardEligible(
            BattleScenarioProfile scenario,
            string contentId)
        {
            return scenario.testOnlyUnconfirmed
                ? CardTestStatus.CanUseInTestBattle(contentId)
                : CardTestStatus.IsReady(contentId);
        }

        private static void AddForcedRunCard(
            List<CardConfig> forced,
            IReadOnlyList<CardConfig> runDeck,
            int instanceId)
        {
            if (instanceId <= 0 || runDeck == null)
                return;
            for (int i = 0; i < runDeck.Count; i++)
            {
                CardConfig card = runDeck[i];
                if (card != null && card.runtimeInstanceId == instanceId)
                {
                    forced.Insert(0, card);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Owns this encounter's transient content from spec acquisition through
    /// combat disposal, including each bound enemy temporary-card template.
    /// Ownership is explicit: the producing spec transfers those templates;
    /// wrapping a borrowed CombatController does not acquire its enemy graph.
    /// Track eagerly so death, escape or behavior replacement cannot hide an
    /// owned template. Deduplicate by object identity, never stable content ID.
    /// All failure and success paths share this one release boundary.
    /// </summary>
    internal sealed class StageEncounterAssembly : IDisposable
    {
        private readonly HashSet<UnityEngine.Object> _ownedObjects =
            new HashSet<UnityEngine.Object>();
        private readonly IReadOnlyList<UnityEngine.Object> _lateOwnedObjects;
        private bool _disposed;

        internal StageEncounterAssembly(EncounterScenarioSpec ownedSpec)
        {
            if (ownedSpec == null) throw new ArgumentNullException(nameof(ownedSpec));
            TrackCards(ownedSpec.OwnedCardPool);
            foreach (EnemyStats enemy in ownedSpec.Enemies ?? Array.Empty<EnemyStats>())
            {
                EnemyBehaviorDefinition behavior = enemy?.behaviorDefinition;
                if (behavior == null) continue;
                foreach (EnemyBehaviorAction action in behavior.EnumerateActions())
                {
                    TrackOwnedObject(action.TemporaryCard.Definition);
                    if (action.IntentType == EnemyIntentType.Compound)
                        TrackOwnedObject(action.Compound.SecondaryTemporaryCard.Definition);
                }
            }
            try
            {
                UnlockRequirements = NaturalLevelUpRewardCatalog.ResolveUnlockRequirements(
                    ownedSpec.OwnedCardPool ?? Array.Empty<CardConfig>());
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public StageEncounterAssembly(
            CombatController combat,
            IReadOnlyList<UnityEngine.Object> lateOwnedObjects,
            params IReadOnlyList<CardConfig>[] ownedCardSets)
        {
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            _lateOwnedObjects = lateOwnedObjects;
            if (ownedCardSets != null)
                for (int i = 0; i < ownedCardSets.Length; i++)
                    TrackCards(ownedCardSets[i]);
            AttachCombat(combat);
        }

        internal void AttachCombat(CombatController combat)
        {
            if (_disposed || Combat != null) throw new InvalidOperationException("Encounter ownership is already closed or attached.");
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            TrackCombatCards();
            // A consumed runtime clone may leave every pile before Dispose.
            Combat.OnCardPlayedVFX += TrackPlayedCard;
            // Unplayed copies also leave every pile at the terminal boundary.
            // Acquire the generated objects when committed, not by late reachability.
            Combat.OnCardsCopied += TrackCopiedCards;
            Combat.OnCardPlayResolved += TrackCreatedCards;
        }

        public CombatController Combat { get; private set; }
        internal CardUnlockRequirements UnlockRequirements { get; } =
            new CardUnlockRequirements(null, null);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Combat != null)
            {
                Combat.OnCardPlayedVFX -= TrackPlayedCard;
                Combat.OnCardsCopied -= TrackCopiedCards;
                Combat.OnCardPlayResolved -= TrackCreatedCards;
            }

            // Rewards and generated cards can enter a pile after construction.
            TrackCombatCards();
            if (_lateOwnedObjects != null)
            {
                for (int i = 0; i < _lateOwnedObjects.Count; i++)
                {
                    UnityEngine.Object item = _lateOwnedObjects[i];
                    TrackOwnedObject(item);
                }
            }
            foreach (UnityEngine.Object item in _ownedObjects)
            {
                if (item != null)
                    UnityEngine.Object.DestroyImmediate(item, allowDestroyingAssets: false);
            }
            _ownedObjects.Clear();
        }

        private void TrackCombatCards()
        {
            if (Combat == null) return;
            TrackCards(Combat.Hand);
            TrackCards(Combat.Deck);
            TrackCards(Combat.Discard);
        }

        internal void TrackCards(IReadOnlyList<CardConfig> cards)
        {
            if (cards == null) return;
            for (int i = 0; i < cards.Count; i++)
            {
                CardConfig card = cards[i];
                TrackOwnedObject(card);
            }
        }

        internal void TrackOwnedObject(UnityEngine.Object item)
        {
            if (item == null) return;
#if UNITY_EDITOR
            // A supplied author asset is a borrowed reference even if it occurs
            // in a transferred content graph. Never unload or destroy the asset.
            if (UnityEditor.EditorUtility.IsPersistent(item)) return;
#endif
            _ownedObjects.Add(item);
        }

        private void TrackPlayedCard(CardConfig card, int ignoredTarget)
        {
            TrackOwnedObject(card);
        }

        private void TrackCopiedCards(CombatCardsCopiedEvent copied) => TrackCards(copied.Cards);

        private void TrackCreatedCards(CombatCardResolvedEvent resolved)
        {
            // Receipts expose identities, not owned references. Capture the new
            // instances before a later terminal boundary removes them from zones.
            if (resolved.CreatedCards.Count > 0) TrackCombatCards();
        }
    }
}
