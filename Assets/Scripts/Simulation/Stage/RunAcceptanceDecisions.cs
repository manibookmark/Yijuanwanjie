using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns the adapter from visible map offers to build-policy intents. Content
    /// and temporary card instances are scoped here; every committed change is
    /// made by the formal Dungeon transaction or the existing typed executor.
    /// </summary>
    internal sealed class RunAcceptanceDecisions : IDisposable
    {
        private readonly DungeonRunSession _run;
        private readonly RunAcceptancePolicy _policy;
        private readonly MetaProgression _account;
        private readonly RunAcceptanceSample _sample;
        private readonly StageSimulationTransactionExecutor _effects;
        private readonly DungeonRunInteractionCatalog _catalog;
        private readonly PlayerStats _basePlayer;
        private readonly bool _ownsPool;
        private readonly RunAcceptanceEconomy _economy;
        private readonly string _rewardVolumeId;
        internal IReadOnlyList<CardConfig> CardPool => _catalog.Cards;

        internal static RunAcceptanceDecisions Open(DungeonRunSession run, RunAcceptancePolicy policy,
            RunAcceptanceEconomy economy, IRandomSource random, RunAcceptanceSample sample,
            Action<PlayerStats> playerProjection = null)
        {
            run.ConfigureSealRewardEligibility(() =>
                WanjieScroll.Progression.SealRewardEligibility.CanReceiveSeals(economy.Account));
            BattleScenarioProfile scenario = ResolveScenario(run.CurrentFloor);
            List<CardConfig> pool = ContentRuntimeFactory.CreateCardPool(scenario.volumeId);
            try
            {
                PlayerStats player = scenario.CreatePlayer();
                playerProjection?.Invoke(player);
                LevelUpRewardRuleResolution rewards = StageEncounterAssembler.ResolveRewardRule(
                    run.CurrentFloor.Profile, scenario, pool, economy.Account);
                var recipes = new List<DungeonRunEvolutionRecipe>();
                if (scenario.volumeId != "common") AddRecipes("common", recipes);
                AddRecipes(scenario.volumeId, recipes);
                var catalog = new DungeonRunInteractionCatalog
                    {
                        Cards = pool, Recipes = recipes, RewardCards = rewards.Cards,
                        GemChestOfferResolver = new WanjieScroll.Progression.DungeonRunGemChestOfferResolver(run, economy.Account, ContentCatalog.Current, allowSilverRunDeckPayment: true),
                        RewardConfig = rewards.Config, IsEligible = CardTestStatus.CanUseInFormalPlay, IsEvolutionResultEligible = CardTestStatus.CanUseEvolutionResult,
                        NaturalRewardPool = rewards.NaturalPool,
                        CanReceiveSeals = WanjieScroll.Progression.SealRewardEligibility.CanReceiveSeals(economy.Account),
                        SealRewardPools = AgentGameInteractions.CreateSealRewardPools(
                            economy.Account?.unlockedSeals ??
                            LingyinCatalog.CreateDefaultUnlockedSealContentIds()),
                        LevelRewardOfferResolver = WanjieScroll.Progression.LevelUpOfferScheduleAdapter.ResolveForFloor(
                            run.CurrentFloor.Profile, pool, CardTestStatus.IsReady,
                            canReceiveSeals: () => WanjieScroll.Progression.SealRewardEligibility.CanReceiveSeals(economy.Account)),
                        GrowthPercent = run.RunProgression.ProjectGrowth(player.mingWu),
                        LuckPercent = run.RunProgression.ProjectLuck(player.tianYun),
                        Wallet = economy.ForGreed(run.RunProgression.ProjectGreed(player.lianYu))
                    };
                run.ConfigureVictorySealRewardPools(catalog.SealRewardPools);
                return new RunAcceptanceDecisions(run, policy, economy.Account, random, sample,
                    catalog, player, true, economy, scenario.volumeId);
            }
            catch { DestroyCards(pool); throw; }
        }

        // This is also the in-memory boundary for offer/commit contract tests.
        internal RunAcceptanceDecisions(DungeonRunSession run, RunAcceptancePolicy policy,
            MetaProgression account, IRandomSource random, RunAcceptanceSample sample,
            DungeonRunInteractionCatalog catalog, PlayerStats basePlayer, bool ownsPool = false,
            RunAcceptanceEconomy economy = null, string rewardVolumeId = null)
        {
            _run = run; _policy = policy; _account = account; _sample = sample;
            _catalog = catalog; _basePlayer = basePlayer; _ownsPool = ownsPool;
            _economy = economy;
            _rewardVolumeId = rewardVolumeId;
            _run.ConfigureSealRewardEligibility(() =>
                WanjieScroll.Progression.SealRewardEligibility.CanReceiveSeals(account));
            _effects = new StageSimulationTransactionExecutor(random, policy, account);
        }

        internal RunAcceptanceDecision ChooseNode()
        {
            List<CardConfig> deck = ReadDeck();
            try
            {
                var options = new List<RunAcceptanceNodeOption>();
                foreach (LevelNode node in _run.CurrentFloor.Nodes)
                {
                    if (node == null || _run.CurrentFloor.GetNodeState(node.contentId) != LevelNodeViewState.Available ||
                        node.nodeType == LevelNodeType.Start) continue;
                    // Author placeholders may be visible after their normal
                    // prerequisite unlock, but they are not executable rewards.
                    // Exclude them from autonomous route choice instead of
                    // misreporting a legal map state as a structural Run error.
                    if (node.nodeType == LevelNodeType.Interaction &&
                        node.completionEffects != null &&
                        node.completionEffects.Any(effect => effect == null ||
                            effect.kind == LevelInteractionEffectKind.Unavailable))
                        continue;
                    if (node.nodeType == LevelNodeType.Interaction && node.completionEffects.Count == 1 &&
                        DungeonRunInteractionTransaction.Handles(node.completionEffects[0].kind))
                    {
                        Require(_run.Interactions.TryInspect(node.contentId, _catalog,
                            out DungeonRunInteractionOffer offer, out string error), error);
                        if (offer.Phase == DungeonRunInteractionPhase.Unavailable) continue;
                    }
                    if (node.nodeType == LevelNodeType.Interaction && node.completionEffects.Count == 1 &&
                        node.completionEffects[0].kind == LevelInteractionEffectKind.ConvertCard)
                    {
                        LevelInteractionEffect effect = node.completionEffects[0];
                        RunCardConversionRuleProfile profile = RunCardConversionRuleCatalog.Get(effect.targetContentId);
                        Require(RunCardConversionChoices.TryInspect(_run, node.contentId, effect, profile, CardPool,
                            out RunCardConversionOffer conversion, out string conversionError), conversionError);
                        // A route policy may skip a presently unaffordable optional
                        // repair, but may not invent an alternate cash reward.
                        if (conversion.Candidates.Count == 0 && !conversion.AllowsCashOut) continue;
                    }
                    bool combat = node.nodeType == LevelNodeType.Encounter || node.nodeType == LevelNodeType.Finale;
                    // A map's boss marker is visible metadata; future lineups and
                    // enemy statistics are deliberately not exposed to the policy.
                    bool boss = node.nodeType == LevelNodeType.Finale ||
                        (combat && !string.IsNullOrEmpty(BattleScenarioCatalog.Get(node.scenarioId).levelBossContentId));
                    var previous = _sample.combats.Where(c => c.floorContentId == _run.CurrentFloorContentId).ToList();
                    options.Add(new RunAcceptanceNodeOption
                    {
                        id = node.contentId, type = node.nodeType, boss = boss,
                        guaranteedBuildReward = node.victoryRewards?.Count > 0,
                        observedHealthLoss = previous.Count == 0 ? 0f :
                            (float)previous.Average(c => Math.Max(0, c.startingHealth - c.endingHealth)),
                        effects = node.completionEffects
                    });
                }
                if (options.Count == 0) throw new InvalidOperationException(
                    "No executable legal node remains before a formal Run conclusion.");
                return Select("Node", string.Empty, _policy.ScoreNodes(Player(), deck, options));
            }
            finally { DestroyCards(deck); }
        }

        internal DungeonRunFloorExitResult ExecuteInteraction(LevelNode node,
            string expectedFloorId = null, Func<string, LevelRunProfile> resolveFloor = null)
        {
            expectedFloorId ??= _run.CurrentFloorContentId;
            if (node.nodeType == LevelNodeType.Exit)
            {
                IDungeonRunInteractionWallet wallet = _economy?.ForGreed(
                    _run.RunProgression.ProjectGreed(_basePlayer.lianYu), JadeSource.Special)
                    ?? _catalog.Wallet;
                Require(_run.FloorExits.TryCommit(expectedFloorId, node.contentId,
                    resolveFloor ?? LevelRunProfileCatalog.Get, wallet,
                    out DungeonRunFloorExitResult result, out string exitError), exitError);
                return result;
            }
            Require(string.Equals(expectedFloorId, _run.CurrentFloorContentId, StringComparison.Ordinal),
                "Simulation interaction belongs to a departed floor.");
            if (node.completionEffects.Count == 1 &&
                DungeonRunInteractionTransaction.Handles(node.completionEffects[0].kind))
            { ExecuteManaged(node); return null; }
            Require(_run.CurrentFloor.TryBeginInteraction(node.contentId, out string error), error);
            foreach (LevelInteractionEffect effect in node.completionEffects)
            {
                if (effect.kind == LevelInteractionEffectKind.ConvertCard) Convert(node, effect);
                else if (effect.kind == LevelInteractionEffectKind.GrantSeal) AssignMapSeal(node, effect);
                else
                {
                    RelicData relic = null;
                    if (effect.kind == LevelInteractionEffectKind.GrantRelic &&
                        (!ContentCatalog.Current.TryGetRelic(effect.targetContentId, out relic) ||
                         !Enum.IsDefined(typeof(RelicType), relic.relicType)))
                        throw new InvalidOperationException("Missing or invalid granted relic " + effect.targetContentId);
                    _effects.ApplyInteractionEffect(_run, node, effect,
                        new StageSimulationStep(node.nodeType == LevelNodeType.Exit
                            ? StageSimulationStepKind.Exit : StageSimulationStepKind.Interaction, node.contentId));
                    if (relic != null)
                        _account.TryUnlockRelic(relic.contentId, (RelicType)relic.relicType, relic.name, relic.description);
                }
            }
            if (_run.CurrentFloor.GetNodeState(node.contentId) != LevelNodeViewState.Completed)
                Require(_run.CurrentFloor.TryCompleteInteraction(node.contentId, out error), error);
            return null;
        }

        private void ExecuteManaged(LevelNode node)
        {
            _catalog.CanReceiveSeals = _run.CanReceiveSealRewards;
            if (_rewardVolumeId != null)
            {
                var rewards = WanjieScroll.Progression.NaturalLevelUpRewardCatalog.ResolveGlobal(
                    CardPool, _rewardVolumeId, _account, CardTestStatus.IsReady,
                    runContext: new CardUnlockRunContext(_run.RunId,
                        () => _run.RunProgression.CardUnlockFacts.Capture(), _run.LeaderCrawlerContentId));
                _catalog.RewardCards = rewards.Cards;
                _catalog.RewardConfig = rewards.Config;
                _catalog.NaturalRewardPool = rewards.NaturalPool;
            }
            Require(_run.Interactions.TryInspect(node.contentId, _catalog,
                out DungeonRunInteractionOffer offer, out string error), error);
            if (offer.Phase == DungeonRunInteractionPhase.Unavailable)
                throw new InvalidOperationException("Selected interaction is unavailable.");
            Require(_run.CurrentFloor.TryBeginInteraction(node.contentId, out error), error);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (offer.Phase != DungeonRunInteractionPhase.Completed)
            {
                string signature = offer.Phase + "/" + offer.SelectedSeal?.contentId + "/" +
                    string.Join("|", offer.Options.Select(o => o.Id));
                // Experience can offer the same candidates at a later level.
                signature += "/" + _run.CombatProgression.CurrentLevel + "/" + _run.RunCardCount;
                if (!seen.Add(signature)) throw new InvalidOperationException("Map choice failed to advance.");
                RunAcceptanceDecision decision = ScoreOffer(node, offer);
                Require(_run.Interactions.TryCommit(node.contentId, decision.selectedId, _catalog,
                    out offer, out error, node.completionEffects[0].kind == LevelInteractionEffectKind.GemChest
                        ? () => { _run.SerializeForDeterministicComparison(); } : (Action)null), error);
                decision.healthAfter = _run.CurrentHealth;
            }
            if (_run.CurrentFloor.GetNodeState(node.contentId) != LevelNodeViewState.Completed)
                Require(_run.CurrentFloor.TryCompleteInteraction(node.contentId, out error), error);
        }

        private RunAcceptanceDecision ScoreOffer(LevelNode node, DungeonRunInteractionOffer offer)
        {
            if (offer.Phase == DungeonRunInteractionPhase.Ready)
                return Select("Ready", node.contentId, new List<RunAcceptanceCandidate> {
                    new RunAcceptanceCandidate { id = string.Empty, reason = "begin-formal-transaction" } });
            List<CardConfig> deck = ReadDeck();
            try
            {
                PlayerStats player = Player();
                var scores = new List<RunAcceptanceCandidate>();
                if (offer.Phase == DungeonRunInteractionPhase.RewardChoice)
                {
                    var rewards = offer.Options.Select(o => new LevelUpSystem.Reward
                    { type = o.Seal == null ? LevelUpSystem.RewardType.NewCard : LevelUpSystem.RewardType.Gem,
                        card = o.Seal == null ? FindCard(o.CardContentId) : null, seal = o.Seal?.Clone() }).ToList();
                    scores = _policy.ScoreRewards(player, deck, rewards);
                    foreach (RunAcceptanceCandidate candidate in scores)
                        candidate.id = offer.Options[candidate.instanceId].Id;
                }
                else foreach (DungeonRunInteractionOption option in offer.Options)
                {
                    float score;
                    CardConfig host = deck.FirstOrDefault(c => c.runtimeInstanceId == option.CardRuntimeInstanceId);
                    if (offer.Phase == DungeonRunInteractionPhase.SealChoice)
                        score = option.Seal == null ? _policy.CashOutValue(node.completionEffects[0].cashOutJade)
                            : deck.Where(c => CardSealRules.CanAssign(c, option.Seal,
                                isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult))
                                .Select(c => _policy.SealValue(player, deck, c, option.Seal))
                                .DefaultIfEmpty(-100000f).Max();
                    else if (offer.Phase == DungeonRunInteractionPhase.SealTarget)
                        score = _policy.SealValue(player, deck, host, offer.SelectedSeal);
                    else if (offer.Phase == DungeonRunInteractionPhase.EvolutionChoice)
                    {
                        // The offer already resolved the legal recipe/result.
                        // This is utility of that offered result, not recipe logic.
                        var remaining = deck.Where(c =>
                            c.runtimeInstanceId != option.CardRuntimeInstanceId).ToList();
                        remaining.Add(FindCard(option.CardContentId));
                        score = _policy.BuildValue(player, remaining) - _policy.BuildValue(player, deck);
                    }
                    else throw new InvalidOperationException("Unsupported map offer phase " + offer.Phase);
                    scores.Add(new RunAcceptanceCandidate { id = option.Id, contentId = option.CardContentId,
                        instanceId = option.CardRuntimeInstanceId, score = score, reason = "change-in-build-value" });
                }
                if (scores.Count == 0 && offer.Phase == DungeonRunInteractionPhase.SealTarget)
                    scores.Add(new RunAcceptanceCandidate { id = "back", reason = "no-legal-host" });
                return Select(offer.Phase.ToString(), node.contentId, scores);
            }
            finally { DestroyCards(deck); }
        }

        private void Convert(LevelNode node, LevelInteractionEffect effect)
        {
            RunCardConversionRuleProfile profile = RunCardConversionRuleCatalog.Get(effect.targetContentId);
            Require(RunCardConversionChoices.TryInspect(_run, node.contentId, effect, profile, CardPool,
                out RunCardConversionOffer offer, out string error), error);
            List<CardConfig> deck = ReadDeck();
            try
            {
                var scores = offer.Candidates.Select(c => new RunAcceptanceCandidate
                {
                    id = c.RuntimeInstanceId.ToString(), instanceId = c.RuntimeInstanceId, contentId = c.ContentId,
                    score = _policy.ConversionValue(Player(), deck, c.RuntimeInstanceId, c.Outcome, c.Amount),
                    reason = "conversion-versus-deck-removal"
                }).ToList();
                if (offer.AllowsCashOut)
                    scores.Add(new RunAcceptanceCandidate { id = "cash-out", score = _policy.CashOutValue(offer.CashOutBaseJade),
                        reason = "retain-deck-and-take-jade" });
                RunAcceptanceDecision decision = Select("Conversion", node.contentId, scores);
                bool success = decision.selectedId == "cash-out"
                    ? RunCardConversionChoices.TryCashOut(_run, node.contentId, effect, profile, CardPool,
                        _catalog.Wallet, out _, out error)
                    : RunCardConversionChoices.TryConvert(_run, node.contentId, effect,
                        scores.First(c => c.id == decision.selectedId).instanceId, profile, CardPool, out _, out error);
                Require(success, error);
                decision.healthAfter = _run.CurrentHealth;
            }
            finally { DestroyCards(deck); }
        }

        private void AssignMapSeal(LevelNode node, LevelInteractionEffect effect)
        {
            if (!_run.CanReceiveSealRewards)
            {
                int baseAmount = new EconomyConfig().cashOutJade;
                var wallet = _economy?.ForGreed(_run.RunProgression.ProjectGreed(_basePlayer.lianYu))
                    ?? _catalog.Wallet;
                Select("GrantSeal", node.contentId, new List<RunAcceptanceCandidate> {
                    new RunAcceptanceCandidate { id = "cash-out", score = _policy.CashOutValue(baseAmount),
                        reason = "seal-feature-not-unlocked" } });
                Require(_run.TryCashOutSealReward(node.contentId, effect, baseAmount, wallet,
                    out _, out string cashOutError), cashOutError);
                return;
            }
            CardSealSpec seal = ResolveSeal(effect.targetContentId);
            List<CardConfig> deck = ReadDeck();
            try
            {
                var scores = SealScores(deck, seal);
                RunAcceptanceDecision decision = Select("GrantSeal", node.contentId, scores);
                int id = scores.First(c => c.id == decision.selectedId).instanceId;
                Require(_run.TryAssignSeal(node.contentId, effect, id, seal, CardPool, out _, out string error,
                    isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult), error);
            }
            finally { DestroyCards(deck); }
        }

        internal void ResolveVictorySeal(string nodeId)
        {
            if (!_run.CurrentFloor.TryGetPendingVictorySealReward(out LevelVictorySealRewardOffer offer)) return;
            if (!string.Equals(nodeId, offer.NodeContentId, StringComparison.Ordinal))
                throw new InvalidOperationException("Pending victory reward belongs to another node.");
            List<CardConfig> deck = ReadDeck();
            try
            {
                // A restored, still-legal selection is kept. Only an unhostable selection
                // with no cash-out option returns through Dungeon's formal candidate page.
                if (offer.Phase == LevelVictorySealRewardPhase.ChoosingSeal ||
                    (!offer.AllowsCashOut && offer.SealContentIds.Count > 1 &&
                     SealScores(deck, ResolveSeal(offer.SealContentId)).Count == 0))
                    offer = ChooseVictorySeal(nodeId, offer, deck);
                // A legally empty pool has no seal identity or host choice.
                // Keep its only decision on the existing cash-out transaction.
                CardSealSpec seal = offer.IsCashOutOnly ? null : ResolveSeal(offer.SealContentId);
                List<RunAcceptanceCandidate> scores = offer.IsCashOutOnly
                    ? new List<RunAcceptanceCandidate>() : SealScores(deck, seal);
                if (offer.AllowsCashOut)
                    scores.Add(new RunAcceptanceCandidate { id = "cash-out",
                        score = _policy.CashOutValue(new EconomyConfig().cashOutJade), reason = "retain-deck-and-take-jade" });
                RunAcceptanceDecision decision = Select("VictorySeal", nodeId, scores);
                if (decision.selectedId == "cash-out")
                {
                    if (offer.Phase == LevelVictorySealRewardPhase.AssigningSeal)
                        Require(_run.CurrentFloor.TryReturnToVictorySealDisposition(offer.RewardContentId,
                            out string returnError), returnError);
                    var economy = new EconomySystem(new EconomyConfig(), _run.RunProgression);
                    Require(_run.TryCashOutVictorySealReward(offer.RewardContentId,
                        () => economy.CashOut(_run.RunProgression.RunGreedBonus), out _, out string error), error);
                }
                else
                {
                    Require(_run.CurrentFloor.TryBeginVictorySealAssignment(offer.RewardContentId, out string error), error);
                    Require(_run.TryAssignVictorySealReward(offer.RewardContentId,
                        scores.First(c => c.id == decision.selectedId).instanceId, seal, CardPool, out _, out error,
                        isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult), error);
                }
                decision.healthAfter = _run.CurrentHealth;
            }
            finally { DestroyCards(deck); }
        }

        private LevelVictorySealRewardOffer ChooseVictorySeal(string nodeId,
            LevelVictorySealRewardOffer offer, List<CardConfig> deck)
        {
            var scores = new List<RunAcceptanceCandidate>();
            foreach (string id in offer.SealContentIds)
            {
                List<RunAcceptanceCandidate> hosts = SealScores(deck, ResolveSeal(id));
                if (hosts.Count == 0 && !offer.AllowsCashOut) continue;
                float score = hosts.Count == 0 ? float.NegativeInfinity : hosts.Max(c => c.score);
                if (offer.AllowsCashOut)
                    score = Math.Max(score, _policy.CashOutValue(new EconomyConfig().cashOutJade));
                scores.Add(new RunAcceptanceCandidate { id = id, contentId = id, score = score,
                    reason = "best-legal-host-or-permitted-cash-out" });
            }
            RunAcceptanceDecision decision = Select("VictorySealChoice", nodeId, scores);
            if (offer.Phase != LevelVictorySealRewardPhase.ChoosingSeal)
                Require(_run.CurrentFloor.TryReturnToVictorySealChoice(offer.RewardContentId,
                    out string returnError), returnError);
            Require(_run.CurrentFloor.TryChooseVictorySeal(offer.RewardContentId, decision.selectedId,
                out string error), error);
            Require(_run.CurrentFloor.TryGetPendingVictorySealReward(out LevelVictorySealRewardOffer selected),
                "Victory seal choice did not preserve its pending transaction.");
            return selected;
        }

        private List<RunAcceptanceCandidate> SealScores(List<CardConfig> deck, CardSealSpec seal)
            => deck.Where(c => CardSealRules.CanAssign(c, seal,
                isEvolutionResultEligible: CardTestStatus.CanUseEvolutionResult)).Select(c => new RunAcceptanceCandidate {
                id = c.runtimeInstanceId.ToString(), contentId = c.contentId, instanceId = c.runtimeInstanceId,
                score = _policy.SealValue(Player(), deck, c, seal), reason = "legal-host-marginal-value" }).ToList();

        private RunAcceptanceDecision Select(string kind, string nodeId, List<RunAcceptanceCandidate> scores)
        {
            RunAcceptanceCandidate chosen = scores.OrderByDescending(c => c.score)
                .ThenBy(c => c.id, StringComparer.Ordinal).FirstOrDefault();
            if (chosen == null) throw new InvalidOperationException("No legal candidate for " + kind);
            var decision = new RunAcceptanceDecision { sequence = _sample.decisions.Count + 1,
                floorContentId = _run.CurrentFloorContentId, nodeContentId = nodeId, kind = kind,
                selectedId = chosen.id, reason = chosen.reason, candidates = scores,
                healthBefore = _run.CurrentHealth, healthAfter = _run.CurrentHealth };
            _sample.decisions.Add(decision);
            return decision;
        }

        internal List<CardConfig> ReadDeck()
        {
            Require(_run.TryGetCurrentCards(CardPool, out List<CardConfig> cards, out string error), error);
            return cards;
        }

        private PlayerStats Player()
        {
            PlayerStats result = JsonUtility.FromJson<PlayerStats>(JsonUtility.ToJson(_basePlayer));
            result.currentHealth = _run.CurrentHealth; result.maxHealth = _run.MaxHealth;
            result.faLi = _run.RunProgression.CardPlayGrowth.ProjectBaseMana(
                Sum(result.faLi, _run.CurrentFloor.Modifiers.BaseManaBonus));
            result.fengRui = Sum(result.fengRui, _run.CurrentFloor.Modifiers.MightBonus);
            result.bengLie = Sum(result.bengLie, _run.CurrentFloor.Modifiers.AreaBonus);
            result.baseArmorPerTurn = Sum(result.baseArmorPerTurn, _run.CurrentFloor.Modifiers.BaseArmorPerTurnBonus);
            return result;
        }

        private CardConfig FindCard(string id) => CardPool.First(c => c != null &&
            string.Equals(c.contentId, id, StringComparison.OrdinalIgnoreCase));
        private static CardSealSpec ResolveSeal(string id)
            => LingyinCatalog.TryGet(id, out LingyinConfig seal) && seal != null
                ? seal.ToCardSealSpec() : throw new InvalidOperationException("Missing seal " + id);
        internal static BattleScenarioProfile ResolveScenario(LevelRunSession floor)
        {
            foreach (LevelNode node in floor.Nodes)
                if (node != null && (node.nodeType == LevelNodeType.Encounter || node.nodeType == LevelNodeType.Finale))
                    return BattleScenarioCatalog.Get(node.scenarioId);
            throw new InvalidOperationException("Floor has no formal scenario.");
        }
        private static void AddRecipes(string volume, List<DungeonRunEvolutionRecipe> output)
        {
            foreach (EvolutionData recipe in ContentCatalog.Current.GetVolume(volume).evolutions)
                output.Add(new DungeonRunEvolutionRecipe { ContentId = recipe.contentId,
                    PrimaryCardContentId = recipe.primaryCardContentId,
                    SecondaryCardContentIds = recipe.secondaryCardContentIds, ResultCardContentId = recipe.resultCardContentId });
        }
        private static int Sum(int first, int second) => (int)Math.Min(int.MaxValue, Math.Max(0L, (long)first + second));
        internal static void Require(bool success, string error)
        { if (!success) throw new InvalidOperationException(error ?? "Formal transaction rejected."); }
        internal static void DestroyCards(IEnumerable<CardConfig> cards)
        { if (cards != null) foreach (CardConfig card in cards) if (card != null) UnityEngine.Object.DestroyImmediate(card); }
        public void Dispose() { if (_ownsPool) DestroyCards(CardPool); }

    }
}
